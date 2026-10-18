using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Renews recurring items. Every minute it looks for finished items (completed tasks, events that are over) that are still marked recurring, creates
/// the next instance (see <see cref="RecurrenceScheduler"/>) and then clears the recurring flag on the finished one.
/// That flag is the "already handled" marker: the new instance carries the recurrence on, so each cycle step has
/// exactly one live link and a second run can never spawn a duplicate. When the cycle has ended (past
/// RecurrenceEndsAtUtc) no instance is created and the flag is cleared, which closes the chain.
/// New instance, flag change, history row and scores are saved in one SaveChanges call (one transaction).
/// </summary>
public class RecurringItemJob : IJob
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly ILogger<RecurringItemJob> _logger;

    public RecurringItemJob(IApplicationDbContext context, ICacheService cache, ILogger<RecurringItemJob> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var nowUtc = DateTime.UtcNow;

        var recurring = await _context.CalendarItems
            .Where(t => t.IsRecurring && (t.Status == TaskStatus.Completed || (t.TaskType == TaskType.Event && t.Status != TaskStatus.Cancelled)))
            .ToListAsync(cancellationToken);

        // A Task is finished when completed; an Event has no "completed" status, so it is finished once it is over.
        var finished = recurring
            .Where(t => t.Status == TaskStatus.Completed || CalendarItemUrgencyScoreFactory.IsPastEvent(t, nowUtc))
            .ToList();

        if (finished.Count == 0)
        {
            return;
        }

        var created = new List<CalendarItem>();
        foreach (var item in finished)
        {
            var next = RecurrenceScheduler.CreateNextInstance(item, nowUtc);
            item.IsRecurring = false; // handled: the successor (if any) carries the recurrence on
            item.UpdatedAtUtc = nowUtc;

            if (next is null)
            {
                continue;
            }

            _context.CalendarItems.Add(next);
            _context.TaskHistories.Add(new TaskHistory { CalendarItemId = next.Id, ChangeType = TaskChangeType.Created });
            created.Add(next);
        }

        // Give the new instances a urgency score right away instead of waiting for the hourly job.
        var changedTaskIds = created.Count == 0
            ? []
            : TeamUrgencyRecalculator.Apply(_context, created, nowUtc, ScoreTriggerSource.ScheduledRecalculation);

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var teamId in finished.Select(t => t.TeamId).Distinct())
        {
            await _cache.RemoveAsync(CacheKeys.TeamTasks(teamId), cancellationToken);
        }
        foreach (var taskId in changedTaskIds)
        {
            await _cache.RemoveAsync(CacheKeys.TaskUrgency(taskId), cancellationToken);
        }

        _logger.LogInformation("RecurringItemJob created {Created} new instances and closed {Closed} cycles.",
            created.Count, finished.Count - created.Count);
    }
}
