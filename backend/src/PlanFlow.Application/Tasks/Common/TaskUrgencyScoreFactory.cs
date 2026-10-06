using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Maps a <see cref="TaskItem"/>'s raw fields onto the normalized [0,1] inputs
/// <see cref="UrgencyScoreCalculator"/> expects, resolves the dependency chain between tasks
/// and builds the resulting audit log entries.
/// </summary>
public static class TaskUrgencyScoreFactory
{
    /// <summary>Time constant of the deadline curve: the component is exp(-daysRemaining / 7).</summary>
    private const double DeadlineDecayDays = 7.0;

    /// <summary>A deadline this close (or already past) triggers the "imminent" score floor.</summary>
    private const double ImminentDeadlineDays = 1.0;

    /// <summary><see cref="TaskItem.ImpactScore"/> is authored on a 0–10 scale.</summary>
    private const double MaxImpactScore = 10.0;

    /// <summary>A blocker inherits this share of the urgency of the task it blocks, per step along the chain.</summary>
    private const double InheritanceDecay = 0.9;

    /// <summary>
    /// Scores every task in <paramref name="activeTasks"/> (one team's non-finished tasks) at once, because
    /// a task's score depends on its neighbours: a blocker inherits urgency from what it blocks, and a
    /// blocked task is lowered while its blocker is unfinished. Returns one ready-to-persist log per task.
    /// </summary>
    public static IReadOnlyDictionary<Guid, UrgencyScoreLog> BuildTeamScoreLogs(
        IReadOnlyCollection<TaskItem> activeTasks, DateTime nowUtc, ScoreTriggerSource triggerSource)
    {
        var byId = activeTasks.ToDictionary(t => t.Id);

        // blocker id -> the active tasks it blocks. A blocker that is finished or missing is not in byId.
        var blockedBy = activeTasks
            .Where(t => t.BlockedByTaskId is { } blockerId && byId.ContainsKey(blockerId))
            .ToLookup(t => t.BlockedByTaskId!.Value);

        var blocking = new Dictionary<Guid, double>();
        var effective = new Dictionary<Guid, double>();
        var visiting = new HashSet<Guid>();

        // Effective (unpenalized) score of a task; its blocking component is 0.9 x the best effective
        // score among the tasks it blocks, which recursively covers the whole chain. The `visiting` set
        // only guards against a corrupted cycle (the API rejects cycles) so this can never loop forever.
        double Effective(TaskItem task)
        {
            if (effective.TryGetValue(task.Id, out var cached))
            {
                return cached;
            }
            if (!visiting.Add(task.Id))
            {
                return 0.0;
            }

            var inherited = 0.0;
            foreach (var downstream in blockedBy[task.Id])
            {
                inherited = Math.Max(inherited, InheritanceDecay * Effective(downstream));
            }

            visiting.Remove(task.Id);
            blocking[task.Id] = inherited;
            return effective[task.Id] = Breakdown(task, inherited, nowUtc).EffectiveScore;
        }

        var logs = new Dictionary<Guid, UrgencyScoreLog>(activeTasks.Count);
        foreach (var task in activeTasks)
        {
            Effective(task);
            var isBlocked = task.BlockedByTaskId is { } blockerId && byId.ContainsKey(blockerId);
            logs[task.Id] = BuildScoreLog(task, blocking.GetValueOrDefault(task.Id), isBlocked, nowUtc, triggerSource);
        }

        return logs;
    }

    /// <summary>
    /// Builds the audit log for one task when its blocking component and blocked state are already known.
    /// Prefer <see cref="BuildTeamScoreLogs"/> in production code, which resolves those from the dependency chain.
    /// </summary>
    public static UrgencyScoreLog BuildScoreLog(
        TaskItem task, double blockingComponent, bool isBlocked, DateTime nowUtc,
        ScoreTriggerSource triggerSource = ScoreTriggerSource.ManualUpdate)
    {
        var breakdown = Breakdown(task, blockingComponent, nowUtc);

        return new UrgencyScoreLog
        {
            TaskItemId = task.Id,
            DeadlineComponent = breakdown.Deadline,
            BlockingComponent = breakdown.Blocking,
            ImpactComponent = breakdown.Impact,
            UserOverrideComponent = breakdown.UserOverride,
            IsBlocked = isBlocked,
            FinalScore = UrgencyScoreCalculator.ApplyBlockedPenalty(breakdown.EffectiveScore, isBlocked),
            TriggerSource = triggerSource
        };
    }

    private readonly record struct ScoreBreakdown(
        double Deadline, double Blocking, double? Impact, double UserOverride, double EffectiveScore);

    private static ScoreBreakdown Breakdown(TaskItem task, double blockingComponent, DateTime nowUtc)
    {
        var deadline = ComputeDeadlineComponent(task.DeadlineUtc, nowUtc);
        var blocking = Math.Clamp(blockingComponent, 0.0, 1.0);
        double? impact = task.ImpactScore is { } rating ? Math.Clamp(rating / MaxImpactScore, 0.0, 1.0) : null;
        var userOverride = ComputeOverrideComponent(task, nowUtc);
        var imminent = task.DeadlineUtc is { } due && (due - nowUtc).TotalDays <= ImminentDeadlineDays;

        var effective = UrgencyScoreCalculator.Calculate(deadline, blocking, impact, userOverride, imminent);
        return new ScoreBreakdown(deadline, blocking, impact, userOverride, effective);
    }

    // Exponential curve: 1.0 when due/overdue, ~0.87 a day before, ~0.65 at 3 days, ~0.37 at a week,
    // ~0.01 at a month. It rises fastest exactly where the decision gets urgent (the last days).
    private static double ComputeDeadlineComponent(DateTime? deadlineUtc, DateTime nowUtc)
    {
        if (deadlineUtc is null)
        {
            return 0.0;
        }

        var daysRemaining = (deadlineUtc.Value - nowUtc).TotalDays;
        if (daysRemaining <= 0)
        {
            return 1.0;
        }

        return Math.Exp(-daysRemaining / DeadlineDecayDays);
    }

    private static double ComputeOverrideComponent(TaskItem task, DateTime nowUtc)
    {
        if (task.ManualUrgencyOverride is not { } overrideValue)
        {
            return 0.0;
        }

        if (task.ManualUrgencyOverrideExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= nowUtc)
        {
            return 0.0;
        }

        return Math.Clamp(overrideValue, 0.0, 1.0);
    }
}
