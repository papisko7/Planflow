using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Notifications;

public class NotificationService : INotificationService
{
    private readonly IApplicationDbContext _context;

    public NotificationService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task NotifyAsync(
        NotificationEvent evt,
        IEnumerable<Guid> recipientUserIds,
        Func<string, EmailContent> build,
        Guid? teamId = null,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default,
        Guid? taskId = null)
    {
        // You never get a notification about something you did yourself.
        var recipientIds = recipientUserIds.Distinct().Where(id => id != actorUserId).ToList();
        if (recipientIds.Count == 0)
        {
            return;
        }

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => recipientIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.DisplayName, Verified = u.EmailVerifiedAtUtc != null })
            .ToListAsync(cancellationToken);

        var preferences = await _context.NotificationPreferences
            .AsNoTracking()
            .Where(p => recipientIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        var mutes = new Dictionary<Guid, (bool Notifications, bool Chat)>();
        if (teamId is not null && !NotificationPolicy.IsMandatory(evt))
        {
            mutes = await _context.TeamMembers
                .AsNoTracking()
                .Where(m => m.TeamId == teamId && recipientIds.Contains(m.UserId))
                .ToDictionaryAsync(m => m.UserId, m => (m.MuteNotifications, m.MuteChat), cancellationToken);
        }

        foreach (var user in users)
        {
            var prefs = preferences.GetValueOrDefault(user.Id) ?? NotificationPreference.CreateDefault(user.Id);

            // A muted team is silent on every channel, not just e-mail.
            if (mutes.TryGetValue(user.Id, out var mute) && NotificationPolicy.IsMutedByTeam(evt, mute.Notifications, mute.Chat))
            {
                continue;
            }

            var content = build(user.DisplayName);

            // Each channel decides on its own: an unconfirmed address blocks only the e-mail, not the in-app entry.
            if (NotificationPolicy.CanReceive(user.Verified, evt) && NotificationPolicy.IsAllowed(prefs, evt))
            {
                QueueEmail(evt, user.Email, content);
            }

            if (NotificationPolicy.IsInboxAllowed(prefs, evt))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    Event = evt,
                    Title = content.Subject,
                    Body = content.Preview,
                    TeamId = teamId,
                    TaskId = taskId,
                    PushState = NotificationPolicy.IsPushAllowed(prefs, evt) ? PushDeliveryState.Pending : PushDeliveryState.NotWanted
                });
            }
        }
    }

    public void QueueDirect(NotificationEvent evt, string toEmail, string recipientName, Func<string, EmailContent> build) =>
        QueueEmail(evt, toEmail, build(recipientName));

    private void QueueEmail(NotificationEvent evt, string toEmail, EmailContent content) =>
        _context.EmailOutbox.Add(new EmailOutboxMessage
        {
            ToEmail = toEmail,
            Subject = content.Subject,
            HtmlBody = content.HtmlBody,
            TextBody = content.TextBody,
            Event = evt
        });
}
