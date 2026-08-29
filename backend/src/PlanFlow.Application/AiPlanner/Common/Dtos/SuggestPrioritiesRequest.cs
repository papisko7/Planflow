namespace PlanFlow.Application.AiPlanner.Common.Dtos;

/// <summary>
/// Request DTO: AI planner to suggest priority adjustments based on semantic analysis.
/// Similar to GeneratePlanRequest but focused on priority re-ranking rather than full plan generation.
/// </summary>
public class SuggestPrioritiesRequest
{
    public required IReadOnlyList<PlanTaskContext> Tasks { get; set; }

    public required string UserContext { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
