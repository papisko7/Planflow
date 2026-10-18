using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Sends the alerts people set on their events and tasks ("30 minutes before the start", "1 day before at 09:00", "2 days before the deadline").
/// Every minute it looks at the reminders of unfinished items whose anchor lies in the next few weeks, asks <see cref="ReminderSchedule"/>
/// when each one goes off and, for those that are due, sends a push + inbox entry + e-mail to everybody concerned: the assignee, the
/// guests / co-contractors and whoever created the item. A reminder is sent once per trigger moment (<see cref="ItemReminder.LastFiredForUtc"/>),
/// so a changed start sends it again for the new moment, and one whose moment is long gone is never caught up.
/// </summary>
[DisallowConcurrentExecution]
public class ItemReminderJob : IJob
{
    /// <summary>
    /// How far around "now" an item's anchor can lie while one of its reminders is still of interest: the longest lead is four weeks
    /// (<see cref="ItemReminder.MaxOffsetMinutes"/>, or <see cref="ItemReminder.MaxDaysBefore"/> days), plus a day for time zones.
    /// </summary>
    private static readonly TimeSpan LookAhead = TimeSpan.FromDays(ItemReminder.MaxDaysBefore + 3);

    private static readonly TimeSpan LookBehind = TimeSpan.FromDays(2);

    private readonly IApplicationDbContext _context;
    private readonly INotificationService _notifications;
    private readonly ILogger<ItemReminderJob> _logger;

    public ItemReminderJob(IApplicationDbContext context, INotificationService notifications, ILogger<ItemReminderJob> logger)
    {
        _context = context;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sent = await RunAsync(DateTime.UtcNow, context.CancellationToken);
        if (sent > 0)
        {
            _logger.LogInformation("ItemReminderJob sent {Sent} reminders.", sent);
        }
    }

    /// <summary>The whole pass for a given moment (a parameter instead of the clock, so tests can pick it). Returns how many reminders went off.</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var from = nowUtc - LookBehind;
        var to = nowUtc + LookAhead;

        // Tracked: a reminder that goes off gets its LastFiredForUtc stamped in the same save as the notifications it produced.
        var reminders = await _context.ItemReminders
            .Include(r => r.CalendarItem)
            .Where(r => r.CalendarItem.Status != TaskStatus.Completed && r.CalendarItem.Status != TaskStatus.Cancelled)
            .Where(r => (r.Anchor == ReminderAnchor.Deadline ? r.CalendarItem.DeadlineUtc : r.CalendarItem.StartUtc) >= from
                     && (r.Anchor == ReminderAnchor.Deadline ? r.CalendarItem.DeadlineUtc : r.CalendarItem.StartUtc) <= to)
            .ToListAsync(cancellationToken);

        if (reminders.Count == 0)
        {
            return 0;
        }

        var itemIds = reminders.Select(r => r.CalendarItemId).Distinct().ToList();
        var assigneeIds = reminders.Select(r => r.CalendarItem.AssignedUserId).OfType<Guid>().Distinct().ToList();

        // A day-form time ("09:00") is the wall clock of the person in charge of the item.
        var zones = (await _context.Users.AsNoTracking()
                .Where(u => assigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, u.TimeZoneId })
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => AllDayTime.ZoneOrUtc(u.TimeZoneId));

        var participantsByItem = (await _context.ItemParticipants.AsNoTracking()
                .Where(p => itemIds.Contains(p.CalendarItemId))
                .Select(p => new { p.CalendarItemId, p.UserId })
                .ToListAsync(cancellationToken))
            .ToLookup(p => p.CalendarItemId, p => p.UserId);

        var creators = (await _context.TaskHistories.AsNoTracking()
                .Where(h => itemIds.Contains(h.CalendarItemId) && h.ChangeType == TaskChangeType.Created && h.ChangedByUserId != null)
                .Select(h => new { h.CalendarItemId, UserId = h.ChangedByUserId!.Value })
                .ToListAsync(cancellationToken))
            .GroupBy(h => h.CalendarItemId)
            .ToDictionary(g => g.Key, g => g.First().UserId);

        var fired = 0;
        foreach (var reminder in reminders)
        {
            var item = reminder.CalendarItem;
            var zone = item.AssignedUserId is { } assignee && zones.TryGetValue(assignee, out var found) ? found : TimeZoneInfo.Utc;

            var trigger = ReminderSchedule.TriggerUtc(item, reminder, zone);
            if (!ReminderSchedule.IsDue(trigger, reminder.LastFiredForUtc, nowUtc))
            {
                continue;
            }

            var recipients = participantsByItem[item.Id].ToList();
            if (item.AssignedUserId is { } owner)
            {
                recipients.Add(owner);
            }

            if (creators.TryGetValue(item.Id, out var creator))
            {
                recipients.Add(creator);
            }

            // The people's own settings (reminders switch, team mute, push / e-mail master switches) are applied by the service.
            var anchorUtc = reminder.Anchor == ReminderAnchor.Deadline ? item.DeadlineUtc!.Value : item.StartUtc!.Value;
            await _notifications.NotifyAsync(
                NotificationEvent.ItemReminder, recipients,
                EmailTemplates.ItemReminder(item.Title, item.TaskType, reminder.Anchor, anchorUtc, item.IsAllDay, reminder.OffsetMinutes, reminder.DaysBefore),
                item.TeamId, actorUserId: null, cancellationToken, item.Id);

            reminder.LastFiredForUtc = trigger;
            fired++;
        }

        if (fired > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return fired;
    }
}
