using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Maps a <see cref="TaskItem"/>'s raw fields onto the five normalized [0,1] inputs
/// <see cref="UrgencyScoreCalculator"/> expects, and builds the resulting audit log entry.
/// This normalization is a placeholder: Phase 2.1 formalizes it (and Phase 2.2 wires a real
/// AI Planner in place of the always-zero <c>aiComponent</c> fallback used here).
/// </summary>
public static class TaskUrgencyScoreFactory
{
    /// <summary>Deadlines beyond this many days out don't add extra deadline pressure.</summary>
    private const double DeadlineHorizonDays = 30.0;

    /// <summary><see cref="TaskItem.ImpactScore"/> is authored on a 0–10 scale.</summary>
    private const double MaxImpactScore = 10.0;

    /// <summary>Number of blocked downstream tasks at which the blocking component saturates to 1.0.</summary>
    private const double BlockingSaturationCount = 5.0;

    /// <summary>
    /// Computes the current urgency score for <paramref name="task"/> and returns a ready-to-persist
    /// <see cref="UrgencyScoreLog"/> capturing every component for auditability.
    /// </summary>
    /// <param name="task">The task to score (its current in-memory field values are used).</param>
    /// <param name="blockedTaskCount">How many other tasks are blocked by this one.</param>
    /// <param name="nowUtc">Reference time for deadline/override-expiry math (injected for testability).</param>
    public static UrgencyScoreLog BuildScoreLog(TaskItem task, int blockedTaskCount, DateTime nowUtc)
    {
        var deadlineComponent = ComputeDeadlineComponent(task.DeadlineUtc, nowUtc);
        const double aiComponent = 0.0;
        var blockingComponent = Math.Clamp(blockedTaskCount / BlockingSaturationCount, 0.0, 1.0);
        var impactComponent = Math.Clamp(task.ImpactScore / MaxImpactScore, 0.0, 1.0);
        var userOverrideComponent = ComputeOverrideComponent(task, nowUtc);

        var finalScore = UrgencyScoreCalculator.Calculate(
            deadlineComponent, aiComponent, blockingComponent, impactComponent, userOverrideComponent);

        return new UrgencyScoreLog
        {
            TaskItemId = task.Id,
            DeadlineComponent = deadlineComponent,
            AiComponent = aiComponent,
            BlockingComponent = blockingComponent,
            ImpactComponent = impactComponent,
            UserOverrideComponent = userOverrideComponent,
            FinalScore = finalScore,
            // Always true until Phase 2.2 wires a real IAiPlannerClient.
            AiFallbackUsed = true
        };
    }

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

        return Math.Clamp(1.0 - daysRemaining / DeadlineHorizonDays, 0.0, 1.0);
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
