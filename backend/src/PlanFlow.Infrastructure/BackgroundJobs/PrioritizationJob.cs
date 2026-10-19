using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Enums;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Recomputes <see cref="PlanFlow.Domain.Entities.CalendarItem.CurrentUrgencyScore"/> for every
/// non-terminal task, since deadline pressure (the heaviest-weighted component, see
/// <see cref="PlanFlow.Domain.Services.UrgencyScoreCalculator"/>) changes purely with the
/// passage of time even when nobody touches the task. It uses the same team-wide rescoring as the
/// task handlers, so dependency effects (inherited urgency, blocked penalty) stay consistent.
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

        var activeTasks = await _context.CalendarItems
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled)
            .ToListAsync(cancellationToken);

        if (activeTasks.Count == 0)
        {
            return;
        }

        var zones = await UserTimeZones.LoadAsync(_context, activeTasks, cancellationToken);
        var absences = await UserAbsences.LoadAsync(_context, activeTasks, nowUtc, cancellationToken);
        var changedTaskIds = TeamUrgencyRecalculator.Apply(
            _context, activeTasks, nowUtc, ScoreTriggerSource.ScheduledRecalculation, zones: zones, absences: absences);
        await _context.SaveChangesAsync(cancellationToken);

        var affectedTeamIds = activeTasks.Select(t => t.TeamId).Distinct().ToList();
        foreach (var teamId in affectedTeamIds)
        {
            await _cache.RemoveAsync(CacheKeys.TeamTasks(teamId), cancellationToken);
        }
        foreach (var taskId in changedTaskIds)
        {
            await _cache.RemoveAsync(CacheKeys.TaskUrgency(taskId), cancellationToken);
        }

        _logger.LogInformation("PrioritizationJob rescored {Count} tasks ({Changed} changed) across {TeamCount} teams.",
            activeTasks.Count, changedTaskIds.Count, affectedTeamIds.Count);
    }
}
