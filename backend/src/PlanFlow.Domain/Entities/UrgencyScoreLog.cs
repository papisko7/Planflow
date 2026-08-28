using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// Snapshot of one Urgency Score computation for a <see cref="TaskItem"/>, capturing every
/// weighted component so the result is explainable and reproducible for the thesis evaluation:
/// FinalScore = 0.35*Deadline + 0.20*Ai + 0.15*Blocking + 0.20*Impact + 0.10*UserOverride.
/// </summary>
public class UrgencyScoreLog : BaseEntity
{
    public Guid TaskItemId { get; set; }
    public TaskItem TaskItem { get; set; } = null!;

    public double DeadlineComponent { get; set; }
    public double AiComponent { get; set; }
    public double BlockingComponent { get; set; }
    public double ImpactComponent { get; set; }
    public double UserOverrideComponent { get; set; }

    public double FinalScore { get; set; }

    /// <summary>True if the AI component fell back to 0 because the AI planner call failed.</summary>
    public bool AiFallbackUsed { get; set; }
}
