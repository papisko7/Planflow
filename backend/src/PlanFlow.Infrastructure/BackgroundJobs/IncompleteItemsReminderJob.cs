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
/// One push a day (inbox + phone, never e-mail) for people who still have items waiting for details - the data the ranking needs
/// and only the user can add (<see cref="CalendarItem.IsDataIncomplete"/>). Only items that are ranked at all count
/// (<see cref="ScoringProfile.Full"/>); a day off or a birthday is never "incomplete".
/// <para>
/// The reminder is sent once per local day, in the user's own morning (<see cref="FirstHour"/>–<see cref="LastHour"/>), and nothing is sent
/// when the count is zero. Because the inbox row also drives the push, a user would otherwise collect one row per day; instead each
/// user keeps a single "living" row that is refreshed (new count, unread again, moved to the top, pushed again).
/// </para>
/// <para>
/// An item is listed in at most <see cref="MaxRemindersPerItem"/> reminders (<see cref="CalendarItem.IncompleteRemindersSent"/>); after that it
/// is no longer nagged about, although it keeps its "needs details" mark in the lists until the user saves it.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public class IncompleteItemsReminderJob : IJob
{
    /// <summary>The reminder is not sent before this local hour (morning) ...</summary>
    public const int FirstHour = 9;

    /// <summary>... and not from this local hour on (evening), so a late job run never wakes anyone up.</summary>
    public const int LastHour = 21;

    /// <summary>How many reminders may list the same item before it is left out for good.</summary>
    public const int MaxRemindersPerItem = 5;

    private readonly IApplicationDbContext _context;
    private readonly ILogger<IncompleteItemsReminderJob> _logger;

    public IncompleteItemsReminderJob(IApplicationDbContext context, ILogger<IncompleteItemsReminderJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sent = await RunAsync(DateTime.UtcNow, context.CancellationToken);
        if (sent > 0)
        {
            _logger.LogInformation("IncompleteItemsReminderJob raised {Sent} reminders.", sent);
        }
    }

    /// <summary>The whole pass for a given moment (a parameter instead of the clock, so tests can pick the hour). Returns how many users were reminded.</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await _context.CalendarItems
            .AsNoTracking()
            .Where(i => i.IsDataIncomplete && i.AssignedUserId != null && i.IncompleteRemindersSent < MaxRemindersPerItem)
            .Where(i => i.Status != TaskStatus.Completed && i.Status != TaskStatus.Cancelled)
            .Where(i => i.EndUtc == null || i.EndUtc > nowUtc) // an event that is over has nothing left to rank
            .Select(i => new Candidate(i.Id, i.TeamId, i.AssignedUserId!.Value, i.Title, i.TaskType, i.EventTemplate))
            .ToListAsync(cancellationToken);

        var byUser = candidates
            .Where(c => EventTemplateRules.For(c.Type, c.Template).Profile == ScoringProfile.Full)
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (byUser.Count == 0)
        {
            return 0;
        }

        var userIds = byUser.Keys.ToList();
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.TimeZoneId })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var preferences = await _context.NotificationPreferences
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        var mutedTeams = (await _context.TeamMembers
                .AsNoTracking()
                .Where(m => userIds.Contains(m.UserId) && m.MuteNotifications)
                .Select(m => new { m.UserId, m.TeamId })
                .ToListAsync(cancellationToken))
            .Select(m => (m.UserId, m.TeamId))
            .ToHashSet();

        var living = (await _context.Notifications
                .Where(n => userIds.Contains(n.UserId) && n.Event == NotificationEvent.IncompleteItemsReminder)
                .ToListAsync(cancellationToken))
            .GroupBy(n => n.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.CreatedAtUtc).First());

        var reminded = 0;
        var listedItemIds = new List<Guid>();
        foreach (var (userId, items) in byUser)
        {
            if (!users.TryGetValue(userId, out var user))
            {
                continue;
            }

            var isNewRow = !preferences.TryGetValue(userId, out var prefs);
            prefs ??= NotificationPreference.CreateDefault(userId);

            if (!NotificationPolicy.IsInboxAllowed(prefs, NotificationEvent.IncompleteItemsReminder))
            {
                continue;
            }

            var localNow = ToLocal(nowUtc, user.TimeZoneId);
            if (localNow.Hour < FirstHour || localNow.Hour >= LastHour)
            {
                continue;
            }

            if (prefs.LastIncompleteReminderSentAtUtc is { } lastSent && ToLocal(lastSent, user.TimeZoneId).Date == localNow.Date)
            {
                continue; // already reminded today (even if they deleted the entry since)
            }

            var counted = items
                .Where(i => !mutedTeams.Contains((userId, i.TeamId)))
                .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (counted.Count == 0)
            {
                continue;
            }

            var content = EmailTemplates.IncompleteItemsReminder(counted.Select(i => i.Title).ToList())(user.DisplayName);
            var single = counted.Count == 1 ? counted[0] : null; // one item opens directly when the entry is tapped

            if (!living.TryGetValue(userId, out var entry))
            {
                entry = new Notification { UserId = userId, Event = NotificationEvent.IncompleteItemsReminder };
                _context.Notifications.Add(entry);
            }

            entry.Title = content.Subject;
            entry.Body = content.Preview;
            entry.TeamId = single?.TeamId;
            entry.TaskId = single?.Id;
            entry.CreatedAtUtc = nowUtc; // moves it to the top of the inbox
            entry.UpdatedAtUtc = nowUtc;
            entry.ReadAtUtc = null;
            entry.PushState = NotificationPolicy.IsPushAllowed(prefs, NotificationEvent.IncompleteItemsReminder)
                ? PushDeliveryState.Pending
                : PushDeliveryState.NotWanted;
            entry.PushAttempts = 0;
            entry.PushNextAttemptAtUtc = nowUtc;
            entry.PushLastError = null;

            prefs.LastIncompleteReminderSentAtUtc = nowUtc;
            if (isNewRow)
            {
                _context.NotificationPreferences.Add(prefs); // the row is created lazily, like everywhere else
            }

            listedItemIds.AddRange(counted.Select(i => i.Id));
            reminded++;
        }

        if (reminded > 0)
        {
            // Every item this reminder listed has now used one of its reminders.
            foreach (var item in await _context.CalendarItems.Where(i => listedItemIds.Contains(i.Id)).ToListAsync(cancellationToken))
            {
                item.IncompleteRemindersSent++;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        return reminded;
    }

    /// <summary>The user's wall-clock time. An unknown or missing zone falls back to UTC rather than skipping the user.</summary>
    private static DateTime ToLocal(DateTime utc, string? timeZoneId)
    {
        var zone = timeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found) ? found : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
    }

    private sealed record Candidate(Guid Id, Guid TeamId, Guid UserId, string Title, TaskType Type, EventTemplate Template);
}
