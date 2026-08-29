using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.AiPlanner.Common;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.AiPlanner.Common.Dtos;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Enums;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Analyzes active tasks using the IAiPlannerClient, fetching AI-assessed urgency scores.
/// Processes tasks in batches to avoid overwhelming the AI service.
/// On API failure/timeout, gracefully falls back to AiAssessmentScore = null (treated as 0 in scoring).
/// </summary>
public class AnalyzeTasksJob : IJob
{
    private readonly IApplicationDbContext _context;
    private readonly IAiPlannerClient _aiClient;
    private readonly ICacheService _cache;
    private readonly ILogger<AnalyzeTasksJob> _logger;

    /// <summary>Maximum tasks per batch to avoid overwhelming the AI API.</summary>
    private const int BatchSize = 20;

    /// <summary>Timeout for each batch request (5 seconds to avoid blocking the job scheduler).</summary>
    private static readonly TimeSpan AiRequestTimeout = TimeSpan.FromSeconds(5);

    public AnalyzeTasksJob(
        IApplicationDbContext context,
        IAiPlannerClient aiClient,
        ICacheService cache,
        ILogger<AnalyzeTasksJob> logger)
    {
        _context = context;
        _aiClient = aiClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var nowUtc = DateTime.UtcNow;

        // Fetch active tasks that either have no assessment or assessment is older than 24 hours.
        var activeTasks = await _context.Tasks
            .Where(t => t.Status != TaskStatus.Done && t.Status != TaskStatus.Cancelled &&
                        (t.AiAssessmentAtUtc == null || t.AiAssessmentAtUtc < nowUtc.AddHours(-24)))
            .ToListAsync(cancellationToken);

        if (activeTasks.Count == 0)
        {
            _logger.LogInformation("AnalyzeTasksJob: no tasks requiring analysis.");
            return;
        }

        _logger.LogInformation("AnalyzeTasksJob: analyzing {Count} tasks in batches of {BatchSize}.",
            activeTasks.Count, BatchSize);

        var affectedTeamIds = new HashSet<Guid>();
        var processedCount = 0;

        // Process tasks in batches.
        for (int i = 0; i < activeTasks.Count; i += BatchSize)
        {
            var batch = activeTasks.Skip(i).Take(BatchSize).ToList();

            try
            {
                await AnalyzeBatch(batch, nowUtc, cancellationToken);
                processedCount += batch.Count;
                batch.ForEach(t => affectedTeamIds.Add(t.TeamId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AnalyzeTasksJob: batch processing failed; falling back batch to zero AI score.");
                FallbackBatch(batch, nowUtc);
                processedCount += batch.Count;
                batch.ForEach(t => affectedTeamIds.Add(t.TeamId));
            }
        }

        // Persist changes.
        await _context.SaveChangesAsync(cancellationToken);

        // Clear relevant caches.
        foreach (var teamId in affectedTeamIds)
        {
            await _cache.RemoveAsync($"team_tasks:{teamId}", cancellationToken);
        }

        foreach (var task in activeTasks)
        {
            await _cache.RemoveAsync($"task_urgency:{task.Id}", cancellationToken);
        }

        _logger.LogInformation(
            "AnalyzeTasksJob: completed. Processed {Count} tasks across {TeamCount} teams.",
            processedCount, affectedTeamIds.Count);
    }

    /// <summary>
    /// Calls the AI Planner for a batch of tasks, updates AiAssessmentScore on success,
    /// or logs error and continues (no throw).
    /// </summary>
    private async Task AnalyzeBatch(List<Domain.Entities.TaskItem> batch, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var userContext = "Batch analysis via AnalyzeTasksJob";

        var request = new SuggestPrioritiesRequest
        {
            Tasks = batch.Select(t => new PlanTaskContext
            {
                TaskId = t.Id,
                Title = t.Title,
                Description = t.Description,
                DeadlineUtc = t.DeadlineUtc,
                BlockedTaskCount = t.BlockedTasks.Count,
                ImpactScore = t.ImpactScore,
                UrgencyScore = t.CurrentUrgencyScore
            }).ToList(),
            UserContext = userContext
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(AiRequestTimeout);

        SuggestPrioritiesResponse response;
        try
        {
            response = await _aiClient.SuggestPrioritiesAsync(request, cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "AnalyzeTasksJob: AI request timed out after {Seconds}s for batch of {Count} tasks.",
                AiRequestTimeout.TotalSeconds, batch.Count);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AnalyzeTasksJob: AI request failed for batch of {Count} tasks.", batch.Count);
            throw;
        }

        // Apply adjustments from AI response.
        var adjustmentMap = response.PriorityAdjustments.ToDictionary(adj => adj.TaskId, adj => adj.SuggestedUrgencyDelta);

        foreach (var task in batch)
        {
            if (adjustmentMap.TryGetValue(task.Id, out var delta))
            {
                // AI suggested a delta adjustment; clamp the new score to [0,1].
                var suggestedScore = Math.Clamp(task.CurrentUrgencyScore + delta, 0.0, 1.0);
                task.AiAssessmentScore = suggestedScore;
                task.AiAssessmentAtUtc = nowUtc;
            }
            else
            {
                // Task was in request but not in response (edge case); mark as assessed with current score.
                task.AiAssessmentScore = task.CurrentUrgencyScore;
                task.AiAssessmentAtUtc = nowUtc;
            }

            // Recalculate and audit the score after AI assessment update.
            var blockedTaskCount = task.BlockedTasks.Count;
            var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount, nowUtc, ScoreTriggerSource.AiAssessment);
            task.CurrentUrgencyScore = scoreLog.FinalScore;
            _context.UrgencyScoreLogs.Add(scoreLog);
        }

        _logger.LogInformation("AnalyzeTasksJob: successfully analyzed {Count} tasks.", batch.Count);
    }

    /// <summary>
    /// Fallback: set AiAssessmentScore to null (defaults to 0 in UrgencyScoreCalculator)
    /// and mark as assessed so we don't retry immediately. Creates audit log with AiFallback trigger.
    /// </summary>
    private void FallbackBatch(List<Domain.Entities.TaskItem> batch, DateTime nowUtc)
    {
        foreach (var task in batch)
        {
            task.AiAssessmentScore = null;
            task.AiAssessmentAtUtc = nowUtc;

            // Record fallback event in audit log.
            var blockedTaskCount = task.BlockedTasks.Count;
            var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount, nowUtc, ScoreTriggerSource.AiFallback);
            task.CurrentUrgencyScore = scoreLog.FinalScore;
            _context.UrgencyScoreLogs.Add(scoreLog);
        }
    }
}
