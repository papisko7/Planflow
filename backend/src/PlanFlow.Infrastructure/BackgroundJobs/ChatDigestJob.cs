using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Enums;
using Quartz;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Chat e-mail digest for people who opted in: one summary ("3 new messages in Team X") for the chat they missed while
/// offline, never one mail per message, and at most one per hour. Anyone with a live chat connection is skipped, because they
/// are already reading the messages in the app. Teams the user muted for chat are left out.
/// </summary>
[DisallowConcurrentExecution]
public class ChatDigestJob : IJob
{
    /// <summary>Minimum time between two digests to the same person.</summary>
    private static readonly TimeSpan MinimumGap = TimeSpan.FromHours(1);

    private readonly IApplicationDbContext _context;
    private readonly IChatPresence _presence;
    private readonly INotificationService _notifications;
    private readonly ILogger<ChatDigestJob> _logger;

    public ChatDigestJob(IApplicationDbContext context, IChatPresence presence, INotificationService notifications, ILogger<ChatDigestJob> logger)
    {
        _context = context;
        _presence = presence;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var nowUtc = DateTime.UtcNow;

        var subscribers = await _context.NotificationPreferences
            .Where(p => p.EmailEnabled && p.ChatDigest)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var preferences in subscribers)
        {
            if (await _presence.IsOnlineAsync(preferences.UserId, cancellationToken))
            {
                // Online means they are seeing the chat live, so everything up to now counts as read.
                preferences.ChatSeenUpToUtc = nowUtc;
                continue;
            }

            if (preferences.LastChatDigestSentAtUtc is { } lastSent && nowUtc - lastSent < MinimumGap)
            {
                continue;
            }

            var since = preferences.ChatSeenUpToUtc ?? preferences.UpdatedAtUtc ?? preferences.CreatedAtUtc;
            var userId = preferences.UserId;

            var teamIds = await _context.TeamMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId && !m.MuteChat && !m.Team.IsPersonal)
                .Select(m => m.TeamId)
                .ToListAsync(cancellationToken);

            var counts = await _context.ChatMessages
                .AsNoTracking()
                .Where(c => teamIds.Contains(c.TeamId) && c.SenderUserId != userId && c.CreatedAtUtc > since)
                .GroupBy(c => c.TeamId)
                .Select(g => new { TeamId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            if (counts.Count == 0)
            {
                continue;
            }

            var names = await _context.Teams
                .AsNoTracking()
                .Where(t => counts.Select(c => c.TeamId).Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
            var lines = counts.Select(c => (names.GetValueOrDefault(c.TeamId, "your team"), c.Count)).ToList();

            await _notifications.NotifyAsync(NotificationEvent.ChatDigest, new[] { userId }, EmailTemplates.ChatDigest(lines),
                cancellationToken: cancellationToken);

            preferences.ChatSeenUpToUtc = nowUtc;
            preferences.LastChatDigestSentAtUtc = nowUtc;
            sent++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        if (sent > 0)
        {
            _logger.LogInformation("ChatDigestJob queued {Sent} digest e-mails.", sent);
        }
    }
}
