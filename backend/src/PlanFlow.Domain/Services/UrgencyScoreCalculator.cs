namespace PlanFlow.Domain.Services;

/// <summary>
/// Pure, deterministic implementation of the Urgency Score formula:
/// FinalScore = 0.35*Deadline + 0.20*Ai + 0.15*Blocking + 0.20*Impact + 0.10*UserOverride.
/// Every component and the final result are clamped to [0.0, 1.0] so the score is always
/// a comparable, well-defined number regardless of what callers pass in.
/// </summary>
public static class UrgencyScoreCalculator
{
    /// <summary>Deadline pressure carries the most weight — an overdue/imminent task should dominate the score.</summary>
    private const double DeadlineWeight = 0.35;

    /// <summary>AI-assessed importance (semantic urgency from the AI Planner, Phase 2.2); second-highest weight.</summary>
    private const double AiWeight = 0.20;

    /// <summary>How many other tasks are blocked by this one; smaller weight since it's a secondary effect.</summary>
    private const double BlockingWeight = 0.15;

    /// <summary>Business/personal impact of the task, independent of timing.</summary>
    private const double ImpactWeight = 0.20;

    /// <summary>Manual user override; kept lowest so users can nudge but not fully hijack the algorithm.</summary>
    private const double UserOverrideWeight = 0.10;

    /// <summary>
    /// Calculates the final Urgency Score from its five weighted components.
    /// Each input is clamped to [0.0, 1.0] before weighting, and the weighted sum is clamped
    /// again at the end to guard against floating-point drift pushing the result outside range.
    /// </summary>
    /// <param name="deadlineComponent">Normalized urgency from time-to-deadline (0 = far away, 1 = overdue).</param>
    /// <param name="aiComponent">Normalized AI-assessed importance (0 = none, falls back to 0 if the AI call failed).</param>
    /// <param name="blockingComponent">Normalized measure of how many tasks this one blocks.</param>
    /// <param name="impactComponent">Normalized business/personal impact.</param>
    /// <param name="userOverrideComponent">Normalized manual override supplied by the user.</param>
    /// <returns>The final Urgency Score, clamped to [0.0, 1.0].</returns>
    public static double Calculate(
        double deadlineComponent,
        double aiComponent,
        double blockingComponent,
        double impactComponent,
        double userOverrideComponent)
    {
        var weightedSum =
            DeadlineWeight * Clamp(deadlineComponent) +
            AiWeight * Clamp(aiComponent) +
            BlockingWeight * Clamp(blockingComponent) +
            ImpactWeight * Clamp(impactComponent) +
            UserOverrideWeight * Clamp(userOverrideComponent);

        return Clamp(weightedSum);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);
}
