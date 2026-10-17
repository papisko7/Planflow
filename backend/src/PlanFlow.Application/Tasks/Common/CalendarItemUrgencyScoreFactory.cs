using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Maps a <see cref="CalendarItem"/>'s raw fields onto the normalized [0,1] inputs
/// <see cref="UrgencyScoreCalculator"/> expects, resolves the dependency chain between tasks
/// and builds the resulting audit log entries.
/// </summary>
public static class CalendarItemUrgencyScoreFactory
{
    /// <summary>Time constant of the deadline curve: the component is exp(-daysRemaining / 7).</summary>
    private const double DeadlineDecayDays = 7.0;

    /// <summary>Task without an effort estimate: the "imminent" floor starts 24h before the deadline (the original fixed rule).</summary>
    private const double DefaultTaskFloorWindowHours = 24.0;

    /// <summary>Event without an effort estimate: the floor starts 1h before it begins.</summary>
    private const double DefaultEventFloorWindowHours = 1.0;

    /// <summary>An event with no end time is assumed to last this long (also decides when it counts as over).</summary>
    private const double DefaultEventDurationHours = 1.0;

    /// <summary>An event never gets a floor window longer than this, however big its effort estimate is.</summary>
    private const double MaxEventFloorWindowHours = 2.0;

    /// <summary><see cref="CalendarItem.ImpactScore"/> is authored on a 0–10 scale.</summary>
    private const double MaxImpactScore = 10.0;

    /// <summary>A blocker inherits this share of the urgency of the task it blocks, per step along the chain.</summary>
    private const double InheritanceDecay = 0.9;

    /// <summary>
    /// Scores every task in <paramref name="activeTasks"/> (one team's non-finished tasks) at once, because
    /// a task's score depends on its neighbours: a blocker inherits urgency from what it blocks, and a
    /// blocked task is lowered while its blocker is unfinished. Returns one ready-to-persist log per task.
    /// </summary>
    public static IReadOnlyDictionary<Guid, UrgencyScoreLog> BuildTeamScoreLogs(
        IReadOnlyCollection<CalendarItem> activeTasks, DateTime nowUtc, ScoreTriggerSource triggerSource)
    {
        var byId = activeTasks.ToDictionary(t => t.Id);

        // A blocker only counts while it is unfinished and not a past event (a finished meeting no longer holds anything up).
        bool StillBlocks(Guid blockerId) => byId.TryGetValue(blockerId, out var blocker) && !IsPastEvent(blocker, nowUtc);

        // blocker id -> the active tasks it blocks.
        var blockedBy = activeTasks
            .Where(t => t.BlockedByTaskId is { } blockerId && StillBlocks(blockerId))
            .ToLookup(t => t.BlockedByTaskId!.Value);

        var blocking = new Dictionary<Guid, double>();
        var effective = new Dictionary<Guid, double>();
        var visiting = new HashSet<Guid>();

        // Effective (unpenalized) score of a task; its blocking component is 0.9 x the best effective
        // score among the tasks it blocks, which recursively covers the whole chain. The `visiting` set
        // only guards against a corrupted cycle (the API rejects cycles) so this can never loop forever.
        double Effective(CalendarItem task)
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
            var isBlocked = task.BlockedByTaskId is { } blockerId && StillBlocks(blockerId);
            logs[task.Id] = BuildScoreLog(task, blocking.GetValueOrDefault(task.Id), isBlocked, nowUtc, triggerSource);
        }

        return logs;
    }

    /// <summary>
    /// Builds the audit log for one task when its blocking component and blocked state are already known.
    /// Prefer <see cref="BuildTeamScoreLogs"/> in production code, which resolves those from the dependency chain.
    /// </summary>
    public static UrgencyScoreLog BuildScoreLog(
        CalendarItem task, double blockingComponent, bool isBlocked, DateTime nowUtc,
        ScoreTriggerSource triggerSource = ScoreTriggerSource.ManualUpdate)
    {
        var breakdown = Breakdown(task, blockingComponent, nowUtc);

        return new UrgencyScoreLog
        {
            CalendarItemId = task.Id,
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

    private static ScoreBreakdown Breakdown(CalendarItem task, double blockingComponent, DateTime nowUtc)
    {
        // A finished event is history, not a decision: it stops generating priority altogether (no alert fatigue
        // from meetings that already happened) and stays in the calendar only as a record.
        if (IsPastEvent(task, nowUtc))
        {
            return new ScoreBreakdown(0.0, 0.0, null, 0.0, 0.0);
        }

        var timing = ResolveTiming(task);
        var deadline = ComputeDeadlineComponent(timing.EffectiveDeadlineUtc, nowUtc);
        var blocking = Math.Clamp(blockingComponent, 0.0, 1.0);
        double? impact = task.ImpactScore is { } rating ? Math.Clamp(rating / MaxImpactScore, 0.0, 1.0) : null;
        var userOverride = ComputeOverrideComponent(task, nowUtc);
        var imminent = timing.TargetUtc is { } target && (target - nowUtc).TotalHours <= timing.FloorWindowHours;

        var effective = UrgencyScoreCalculator.Calculate(deadline, blocking, impact, userOverride, imminent);
        return new ScoreBreakdown(deadline, blocking, impact, userOverride, effective);
    }

    /// <param name="TargetUtc">The moment that matters: a Task's deadline, an Event's start.</param>
    /// <param name="EffectiveDeadlineUtc">When the clock really runs out: a Task must START by deadline minus its effort; an Event by its start.</param>
    /// <param name="FloorWindowHours">How long before <paramref name="TargetUtc"/> the item counts as "imminent" and gets the score floor.</param>
    private readonly record struct Timing(DateTime? TargetUtc, DateTime? EffectiveDeadlineUtc, double FloorWindowHours);

    // Task:  effectiveDeadline = deadline - effort, so a 6h job due in 5h is already "overdue" (it cannot be finished anymore).
    //        The floor window is the effort itself (24h when no estimate, which equals the original fixed rule).
    // Event: happens at StartUtc; effort is not subtracted because an event cannot be started earlier. The floor window is
    //        the effort/prep time, 1h by default, capped at 2h. Once its end has passed the score is 0 (see Breakdown).
    private static Timing ResolveTiming(CalendarItem task)
    {
        if (task.TaskType == TaskType.Event)
        {
            var start = task.StartUtc;
            var window = Math.Min(task.EstimatedEffortHours ?? DefaultEventFloorWindowHours, MaxEventFloorWindowHours);
            return new Timing(start, start, window);
        }

        var effort = Math.Max(task.EstimatedEffortHours ?? 0.0, 0.0);
        return new Timing(
            task.DeadlineUtc,
            task.DeadlineUtc?.AddHours(-effort),
            task.EstimatedEffortHours ?? DefaultTaskFloorWindowHours);
    }

    /// <summary>When an event ends: its EndUtc, or one default-duration after the start when no end was given.</summary>
    public static DateTime? ResolveEventEnd(DateTime? startUtc, DateTime? endUtc) =>
        endUtc ?? startUtc?.AddHours(DefaultEventDurationHours);

    /// <summary>True for an Event whose end has passed. A Task never counts as past - an overdue Task stays at maximum urgency.</summary>
    public static bool IsPastEvent(CalendarItem item, DateTime nowUtc) =>
        item.TaskType == TaskType.Event && ResolveEventEnd(item.StartUtc, item.EndUtc) is { } end && end < nowUtc;

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

    private static double ComputeOverrideComponent(CalendarItem task, DateTime nowUtc)
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
