using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Recomputes <see cref="PlanFlow.Domain.Entities.TaskItem.CurrentUrgencyScore"/> for every
/// non-terminal task, since deadline pressure (the heaviest-weighted component, see
/// <see cref="PlanFlow.Domain.Services.UrgencyScoreCalculator"/>) changes purely with the
/// passage of time even when nobody touches the task.
/// </summary>
public class PrioritizationJob : IJob
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly ILogger<PrioritizationJob> _logger;

    public PrioritizationJob(IApplicationDbContext context, ICacheService cache, ILogger<PrioritizationJob> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var nowUtc = DateTime.UtcNow;

        var activeTasks = await _context.Tasks
            .Where(t => t.Status != TaskStatus.Done && t.Status != TaskStatus.Cancelled)
            .ToListAsync(cancellationToken);

        if (activeTasks.Count == 0)
        {
            return;
        }

        var blockedCounts = await _context.Tasks
            .Where(t => t.BlockedByTaskId != null)
            .GroupBy(t => t.BlockedByTaskId!.Value)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TaskId, g => g.Count, cancellationToken);

        var affectedTeamIds = new HashSet<Guid>();

        foreach (var task in activeTasks)
        {
            var blockedCount = blockedCounts.GetValueOrDefault(task.Id, 0);
            var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedCount, nowUtc);

            task.CurrentUrgencyScore = scoreLog.FinalScore;
            task.UpdatedAtUtc = nowUtc;
            _context.UrgencyScoreLogs.Add(scoreLog);

            affectedTeamIds.Add(task.TeamId);
            await _cache.RemoveAsync(CacheKeys.TaskUrgency(task.Id), cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var teamId in affectedTeamIds)
        {
            await _cache.RemoveAsync(CacheKeys.TeamTasks(teamId), cancellationToken);
        }

        _logger.LogInformation("PrioritizationJob recalculated urgency for {Count} tasks across {TeamCount} teams.",
            activeTasks.Count, affectedTeamIds.Count);
    }
}
