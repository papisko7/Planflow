namespace PlanFlow.Domain.Enums;

/// <summary>
/// How an item takes part in the Urgency Score. Impact/Effort only make sense where there is a decision
/// ("do it, and when?"), so vacation or a working location are shown in the calendar but never ranked.
/// </summary>
public enum ScoringProfile
{
    /// <summary>All components, Impact and Effort, may be flagged "data incomplete" (Default event, Task).</summary>
    Full = 0,

    /// <summary>No score, no Impact/Effort, never incomplete, outside the ranking; only provides availability context.</summary>
    Informational = 1
}
