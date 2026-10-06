namespace PlanFlow.Domain.Services;

/// <summary>
/// Pure, deterministic implementation of the Urgency Score formula. In plain words:
/// 1) a weighted sum of four factors: 0.45*Deadline + 0.20*Blocking + 0.25*Impact + 0.10*UserOverride;
/// 2) two floors so important things cannot sink: a deadline within 24h (or overdue) keeps the score
///    at 0.85 or more, and a task never scores below the urgency it inherits from the work it blocks;
/// 3) a blocked task (its blocker is not finished yet) is halved, because it cannot be started now
///    and so should drop down the "what do I do next" list.
/// An unrated Impact (null, e.g. a task imported from Google Calendar) does not count as zero: its
/// weight is shared proportionally between the other factors, so missing data never punishes a task.
/// Every component and the final result are clamped to [0.0, 1.0].
/// </summary>
public static class UrgencyScoreCalculator
{
    /// <summary>Deadline pressure carries the most weight — an overdue/imminent task should dominate the score.</summary>
    private const double DeadlineWeight = 0.45;

    /// <summary>Urgency inherited from the tasks this one blocks; a secondary effect.</summary>
    private const double BlockingWeight = 0.20;

    /// <summary>Business/personal impact of the task, independent of timing.</summary>
    private const double ImpactWeight = 0.25;

    /// <summary>Manual user override; kept lowest so users can nudge but not fully hijack the algorithm.</summary>
    private const double UserOverrideWeight = 0.10;

    /// <summary>Minimum score of a task that is overdue or due within 24 hours, so nothing else can outrank it.</summary>
    public const double ImminentDeadlineFloor = 0.85;

    /// <summary>A blocked task keeps this share of its score while its blocker is unfinished.</summary>
    public const double BlockedTaskFactor = 0.5;

    /// <summary>
    /// Calculates the unblocked Urgency Score ("effective" score) from the factors.
    /// This is also the value a blocker inherits from the tasks it blocks.
    /// </summary>
    /// <param name="deadlineComponent">Normalized urgency from time-to-deadline (0 = no deadline/far away, 1 = overdue).</param>
    /// <param name="blockingComponent">Urgency inherited from the (transitively) blocked tasks.</param>
    /// <param name="impactComponent">Normalized impact, or null when the task has not been rated.</param>
    /// <param name="userOverrideComponent">Normalized manual override supplied by the user.</param>
    /// <param name="deadlineImminent">True when the deadline is overdue or within 24 hours.</param>
    public static double Calculate(
        double deadlineComponent,
        double blockingComponent,
        double? impactComponent,
        double userOverrideComponent,
        bool deadlineImminent = false)
    {
        var weightedSum =
            DeadlineWeight * Clamp(deadlineComponent) +
            BlockingWeight * Clamp(blockingComponent) +
            UserOverrideWeight * Clamp(userOverrideComponent);

        // Unrated impact: renormalize so the remaining weights again add up to 1.
        weightedSum = impactComponent is { } impact
            ? weightedSum + ImpactWeight * Clamp(impact)
            : weightedSum / (1.0 - ImpactWeight);

        var score = Clamp(weightedSum);

        if (deadlineImminent)
        {
            score = Math.Max(score, ImminentDeadlineFloor);
        }

        // Priority inheritance: whoever blocks an urgent task is at least as urgent as it is.
        return Math.Max(score, Clamp(blockingComponent));
    }

    /// <summary>The score a task actually shows: halved while it is blocked by an unfinished task.</summary>
    public static double ApplyBlockedPenalty(double effectiveScore, bool isBlocked) =>
        isBlocked ? Clamp(effectiveScore * BlockedTaskFactor) : Clamp(effectiveScore);

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);
}
