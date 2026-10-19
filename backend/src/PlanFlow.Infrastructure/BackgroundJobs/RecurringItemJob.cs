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
/// Keeps repeating items going (the rule and the series are described in <see cref="TaskSeries"/>). Every minute it:
/// <list type="number">
/// <item><b>tops up event series</b> that reach less than 80 days ahead, so every event has its occurrences created for the next 90 days;</item>
/// <item><b>renews task series</b>: a completed task whose series has no later occurrence gets the next one (never an overdue copy);</item>
/// <item>removes series that have no occurrences left.</item>
/// </list>
/// The series' cursor (<see cref="RecurrenceSeries.GeneratedThroughUtc"/>) is the "already handled" marker, so a second run never creates a duplicate
/// and an occurrence the user deleted is not made again. New occurrences, cursor moves and scores are saved in one SaveChanges (one transaction).
/// </summary>
[DisallowConcurrentExecution]
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

        var created = new List<CalendarItem>();
        created.AddRange(await TopUpEventSeriesAsync(nowUtc, cancellationToken));
        created.AddRange(await RenewTaskSeriesAsync(nowUtc, cancellationToken));
        var removed = await RemoveEmptySeriesAsync(cancellationToken);

        if (created.Count == 0 && removed == 0)
        {
            return;
        }

        // Give the new occurrences an urgency score right away instead of waiting for the hourly job.
        var changedTaskIds = created.Count == 0
            ? []
            : TeamUrgencyRecalculator.Apply(_context, created, nowUtc, ScoreTriggerSource.ScheduledRecalculation);

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var teamId in created.Select(t => t.TeamId).Distinct())
        {
            await _cache.RemoveAsync(CacheKeys.TeamTasks(teamId), cancellationToken);
        }
        foreach (var taskId in changedTaskIds)
        {
            await _cache.RemoveAsync(CacheKeys.TaskUrgency(taskId), cancellationToken);
        }

        _logger.LogInformation("RecurringItemJob created {Created} occurrences and removed {Removed} empty series.", created.Count, removed);
    }

    private async Task<List<CalendarItem>> TopUpEventSeriesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var refillBefore = nowUtc + TaskSeries.RefillBelow;
        var due = await _context.RecurrenceSeries
            .Where(s => s.TaskType == TaskType.Event && s.GeneratedThroughUtc < refillBefore
                && (s.EndsAtUtc == null || s.GeneratedThroughUtc < s.EndsAtUtc))
            .ToListAsync(cancellationToken);

        var created = new List<CalendarItem>();
        foreach (var series in due)
        {
            var template = await TaskSeries.LoadTemplateAsync(_context, series.Id, cancellationToken);
            if (template is null)
            {
                continue; // nothing left to copy; RemoveEmptySeriesAsync deals with it
            }

            // An occurrence the user moved past the cursor may sit on a date the rule is about to produce: that date is already taken.
            var taken = (await _context.CalendarItems
                .Where(i => i.RecurrenceSeriesId == series.Id && i.StartUtc > series.GeneratedThroughUtc)
                .Select(i => i.StartUtc!.Value)
                .ToListAsync(cancellationToken)).ToHashSet();

            created.AddRange(TaskSeries.CreateAhead(_context, series, template, AlertsAndPeople.Of(template), nowUtc, taken));
        }

        return created;
    }

    private async Task<List<CalendarItem>> RenewTaskSeriesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        // A completed task is the end of its chain when the series has not moved past it (cursor <= its deadline) and has not ended.
        var finished = await _context.CalendarItems
            .Include(t => t.RecurrenceSeries)
            .Where(t => t.TaskType == TaskType.Task && t.Status == TaskStatus.Completed && t.RecurrenceSeriesId != null
                && t.RecurrenceSeries!.GeneratedThroughUtc <= t.DeadlineUtc
                && (t.RecurrenceSeries.EndsAtUtc == null || t.RecurrenceSeries.GeneratedThroughUtc < t.RecurrenceSeries.EndsAtUtc))
            .ToListAsync(cancellationToken);

        var created = new List<CalendarItem>();
        foreach (var item in finished.GroupBy(t => t.RecurrenceSeriesId))
        {
            var series = item.First().RecurrenceSeries!;
            var latest = item.OrderByDescending(t => t.DeadlineUtc).First();
            var template = await TaskSeries.LoadTemplateAsync(_context, series.Id, cancellationToken) ?? latest;

            var next = TaskSeries.CreateNextTask(_context, series, template, AlertsAndPeople.Of(template), latest.DeadlineUtc!.Value, nowUtc);
            if (next is not null)
            {
                created.Add(next);
            }
        }

        return created;
    }

    /// <summary>A series whose last occurrence was deleted has nothing to repeat from; its row goes too.</summary>
    private async Task<int> RemoveEmptySeriesAsync(CancellationToken cancellationToken)
    {
        var empty = await _context.RecurrenceSeries
            .Where(s => !_context.CalendarItems.Any(i => i.RecurrenceSeriesId == s.Id))
            .ToListAsync(cancellationToken);
        _context.RecurrenceSeries.RemoveRange(empty);
        return empty.Count;
    }
}
