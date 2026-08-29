namespace PlanFlow.Application.AiPlanner.Common.Dtos;

/// <summary>
/// Response DTO: AI planner's suggested task execution plan with explanations.
/// Each recommended task includes reasoning (e.g., "high blocking impact" or "deadline urgency").
/// </summary>
public class GeneratePlanResponse
{
    public required IReadOnlyList<RecommendedTask> RecommendedTasks { get; set; }

    public required string ExecutiveSummary { get; set; }

    public double Confidence { get; set; }

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Single task recommendation: task ID, priority rank, and reasoning.
/// </summary>
public class RecommendedTask
{
    public required Guid TaskId { get; set; }

    public int PriorityRank { get; set; }

    public double SuggestedUrgencyScore { get; set; }

    public required string Reasoning { get; set; }
}
