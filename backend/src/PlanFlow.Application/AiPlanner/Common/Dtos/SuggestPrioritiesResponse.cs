namespace PlanFlow.Application.AiPlanner.Common.Dtos;

/// <summary>
/// Response DTO: AI's suggested priority adjustments.
/// Contains a set of task IDs ranked by recommended priority, with reasoning per task.
/// </summary>
public class SuggestPrioritiesResponse
{
    public required IReadOnlyList<PriorityAdjustment> PriorityAdjustments { get; set; }

    public required string SummaryReasoning { get; set; }

    public double Confidence { get; set; }

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Individual priority adjustment suggestion for one task.
/// </summary>
public class PriorityAdjustment
{
    public required Guid TaskId { get; set; }

    public double SuggestedUrgencyDelta { get; set; }

    public required string AdjustmentReason { get; set; }
}
