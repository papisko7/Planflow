using PlanFlow.Application.AiPlanner.Common.Dtos;

namespace PlanFlow.Application.AiPlanner.Common;

/// <summary>
/// Abstraction for AI-driven task planning and priority suggestions.
/// Implementations must be deterministic within a session (same input = same output).
/// Supports multiple providers (Mock, Claude, OpenAI) via DI.
/// </summary>
public interface IAiPlannerClient
{
    /// <summary>
    /// Generates a complete task execution plan based on task context and user preferences.
    /// Pure semantic analysis: no side effects, no DB writes.
    /// </summary>
    Task<GeneratePlanResponse> GeneratePlanAsync(GeneratePlanRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Suggests priority adjustments for tasks based on semantic analysis.
    /// Lighter weight than full plan generation.
    /// </summary>
    Task<SuggestPrioritiesResponse> SuggestPrioritiesAsync(SuggestPrioritiesRequest request, CancellationToken cancellationToken);
}
