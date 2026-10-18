namespace PlanFlow.Domain.Services;

/// <summary>
/// Pure, deterministic implementation of the Urgency Score formula (algorithm version 2). In plain words:
/// 1) a weighted sum of five factors: 0.40*Deadline + 0.20*Blocking + 0.25*Impact + 0.10*UserOverride + 0.05*Scheduled;
/// 2) two floors so important things cannot sink: an imminent deadline (overdue, or inside a window that
///    depends on the item's type and effort estimate, 24h by default for a task) keeps the score
///    at 0.85 or more, and a task never scores below the urgency it inherits from the work it blocks;
/// 3) a blocked task (its blocker is not finished yet) is halved, because it cannot be started now
///    and so should drop down the "what do I do next" list.
/// A factor that does not apply is not counted as zero: its weight is shared proportionally between the factors that do.
/// This covers an unrated Impact (null, e.g. a task imported from Google) and the Scheduled factor, which exists only for
/// a task that has a planned start, so missing data never punishes an item.
/// Every component and the final result are clamped to [0.0, 1.0].
/// </summary>
public static class UrgencyScoreCalculator
{
    /// <summary>
    /// Version of the weights and rules below, stored on every score log so history from before and after a change is never
    /// compared blindly. 1 = the original four weights (0.45 / 0.20 / 0.25 / 0.10); 2 = adds Scheduled, with no floor and no
    /// alerts for unverified items, and informational items out of the ranking.
    /// </summary>
    public const int AlgorithmVersion = 2;

    /// <summary>Deadline pressure carries the most weight — an overdue/imminent task should dominate the score.</summary>
    private const double DeadlineWeight = 0.40;

    /// <summary>Urgency inherited from the tasks this one blocks; a secondary effect.</summary>
    private const double BlockingWeight = 0.20;

    /// <summary>Business/personal impact of the task, independent of timing.</summary>
    private const double ImpactWeight = 0.25;

    /// <summary>Manual user override; kept low so users can nudge but not fully hijack the algorithm.</summary>
    private const double UserOverrideWeight = 0.10;

    /// <summary>How close the planned start of a task is — a small push to begin what was meant to begin by now.</summary>
    private const double ScheduledWeight = 0.05;

    /// <summary>Minimum score of an item that is overdue or inside its imminent window, so nothing else can outrank it.</summary>
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
    /// <param name="deadlineImminent">True when the deadline is overdue or inside its imminent window (decided by the caller; false for an unverified item).</param>
    /// <param name="scheduledComponent">Normalized closeness of the planned start (1 = it is time to start), or null when the item has none or it does not count.</param>
    public static double Calculate(
        double deadlineComponent,
        double blockingComponent,
        double? impactComponent,
        double userOverrideComponent,
        bool deadlineImminent = false,
        double? scheduledComponent = null)
    {
        var weightedSum =
            DeadlineWeight * Clamp(deadlineComponent) +
            BlockingWeight * Clamp(blockingComponent) +
            UserOverrideWeight * Clamp(userOverrideComponent);
        var presentWeight = DeadlineWeight + BlockingWeight + UserOverrideWeight;

        // A factor that is absent is left out and the remaining weights are scaled back up to add to 1.
        if (impactComponent is { } impact)
        {
            weightedSum += ImpactWeight * Clamp(impact);
            presentWeight += ImpactWeight;
        }
        if (scheduledComponent is { } scheduled)
        {
            weightedSum += ScheduledWeight * Clamp(scheduled);
            presentWeight += ScheduledWeight;
        }

        var score = Clamp(weightedSum / presentWeight);

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
