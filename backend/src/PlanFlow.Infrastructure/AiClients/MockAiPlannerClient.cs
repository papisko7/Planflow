using PlanFlow.Application.AiPlanner.Common;
using PlanFlow.Application.AiPlanner.Common.Dtos;

namespace PlanFlow.Infrastructure.AiClients;

/// <summary>
/// Deterministic mock AI client for testing and offline development.
/// Returns predictable responses based on input task count and urgency scores.
/// Allows injection of custom responses or forced errors for edge case testing.
/// </summary>
public class MockAiPlannerClient : IAiPlannerClient
{
    private GeneratePlanResponse? _customGeneratePlanResponse;
    private SuggestPrioritiesResponse? _customSuggestPrioritiesResponse;
    private Exception? _nextException;

    public async Task<GeneratePlanResponse> GeneratePlanAsync(GeneratePlanRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken); // Simulate network latency

        if (_nextException != null)
        {
            var ex = _nextException;
            _nextException = null;
            throw ex;
        }

        if (_customGeneratePlanResponse != null)
        {
            var response = _customGeneratePlanResponse;
            _customGeneratePlanResponse = null;
            return response;
        }

        // Default deterministic behavior: rank tasks by urgency score descending
        var sortedTasks = request.Tasks.OrderByDescending(t => t.UrgencyScore).ToList();
        var recommendations = sortedTasks
            .Select((task, index) => new RecommendedTask
            {
                TaskId = task.TaskId,
                PriorityRank = index + 1,
                SuggestedUrgencyScore = Math.Min(1.0, task.UrgencyScore + (0.1 * (1.0 - index / (double)sortedTasks.Count))),
                Reasoning = GenerateMockReasoning(task)
            })
            .ToList();

        return new GeneratePlanResponse
        {
            RecommendedTasks = recommendations,
            ExecutiveSummary = $"Processed {request.Tasks.Count} tasks. Top priority: {recommendations.FirstOrDefault()?.TaskId.ToString() ?? "N/A"}",
            Confidence = 0.85,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    public async Task<SuggestPrioritiesResponse> SuggestPrioritiesAsync(SuggestPrioritiesRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(30, cancellationToken);

        if (_nextException != null)
        {
            var ex = _nextException;
            _nextException = null;
            throw ex;
        }

        if (_customSuggestPrioritiesResponse != null)
        {
            var response = _customSuggestPrioritiesResponse;
            _customSuggestPrioritiesResponse = null;
            return response;
        }

        // Default: suggest small urgency deltas based on blocking count and impact
        var adjustments = request.Tasks
            .Select(task => new PriorityAdjustment
            {
                TaskId = task.TaskId,
                SuggestedUrgencyDelta = (task.BlockedTaskCount * 0.05) + (task.ImpactScore * 0.02),
                AdjustmentReason = $"High blocking ({task.BlockedTaskCount} blocked tasks) and impact ({task.ImpactScore}/10)"
            })
            .ToList();

        return new SuggestPrioritiesResponse
        {
            PriorityAdjustments = adjustments,
            SummaryReasoning = "Priorities adjusted based on blocking and impact factors",
            Confidence = 0.80,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Injects a custom GeneratePlan response for the next call (consumed once).
    /// </summary>
    public void SetNextGeneratePlanResponse(GeneratePlanResponse response)
    {
        _customGeneratePlanResponse = response;
    }

    /// <summary>
    /// Injects a custom SuggestPriorities response for the next call (consumed once).
    /// </summary>
    public void SetNextSuggestPrioritiesResponse(SuggestPrioritiesResponse response)
    {
        _customSuggestPrioritiesResponse = response;
    }

    /// <summary>
    /// Forces the next call (Generate or Suggest) to throw the given exception.
    /// </summary>
    public void SetNextException(Exception exception)
    {
        _nextException = exception;
    }

    private static string GenerateMockReasoning(PlanTaskContext task)
    {
        var factors = new List<string>();

        if (task.DeadlineUtc.HasValue)
        {
            var daysRemaining = (task.DeadlineUtc.Value - DateTime.UtcNow).TotalDays;
            if (daysRemaining < 1)
                factors.Add("urgent deadline");
            else if (daysRemaining < 7)
                factors.Add("approaching deadline");
        }

        if (task.BlockedTaskCount > 0)
            factors.Add($"blocking {task.BlockedTaskCount} tasks");

        if (task.ImpactScore >= 8)
            factors.Add("high impact");

        return factors.Any()
            ? $"Recommended due to: {string.Join(", ", factors)}"
            : "Recommended based on overall urgency score";
    }
}
