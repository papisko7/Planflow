namespace PlanFlow.Application.AiPlanner.Common.Dtos;

/// <summary>
/// Request DTO for AI planner to generate a task execution plan.
/// Contains all task context needed for semantic analysis and priority suggestions.
/// </summary>
public class GeneratePlanRequest
{
    public required IReadOnlyList<PlanTaskContext> Tasks { get; set; }

    public required string UserContext { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Minimal task context for AI analysis: deadline, blocking count, impact, urgency.
/// Does NOT include full task record—AI sees only what's needed for semantic reasoning.
/// </summary>
public class PlanTaskContext
{
    public required Guid TaskId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public DateTime? DeadlineUtc { get; set; }

    public int BlockedTaskCount { get; set; }

    public int ImpactScore { get; set; }

    public double UrgencyScore { get; set; }
}
