using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Interfaces;
using Quartz;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Prunes append-only/ephemeral rows that would otherwise grow unbounded:
/// old <see cref="PlanFlow.Domain.Entities.TaskHistory"/> audit entries and already-read alerts.
/// Runs once daily at low-traffic hours (02:00 UTC) since it's a bulk delete, not latency-sensitive.
/// </summary>
public class CleanupJob : IJob
{
    /// <summary>TaskHistory rows older than this are pruned (audit trail retention window).</summary>
    private const int TaskHistoryRetentionDays = 180;

    /// <summary>Read alerts older than this are pruned; unread alerts are never deleted by this job.</summary>
    private const int ReadAlertRetentionDays = 30;

    /// <summary>Delivered mail rows are only kept briefly (debugging); rows that failed for good are kept a bit longer.</summary>
    private const int SentEmailRetentionDays = 7;
    private const int FailedEmailRetentionDays = 30;

    /// <summary>Inbox entries: read ones are pruned after a month, unread ones after three (nobody scrolls back that far).</summary>
    private const int ReadNotificationRetentionDays = 30;
    private const int AnyNotificationRetentionDays = 90;

    /// <summary>A phone that has not signed in for this long is dropped (a signed-out or abandoned device).</summary>
    private const int PushTokenRetentionDays = 120;

    private readonly IApplicationDbContext _context;
    private readonly ILogger<CleanupJob> _logger;

    public CleanupJob(IApplicationDbContext context, ILogger<CleanupJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var nowUtc = DateTime.UtcNow;

        var historyCutoffUtc = nowUtc.AddDays(-TaskHistoryRetentionDays);
        var staleHistory = await _context.TaskHistories
            .Where(h => h.CreatedAtUtc < historyCutoffUtc)
            .ToListAsync(cancellationToken);
        _context.TaskHistories.RemoveRange(staleHistory);

        var alertCutoffUtc = nowUtc.AddDays(-ReadAlertRetentionDays);
        var staleAlerts = await _context.Alerts
            .Where(a => a.IsRead && a.ReadAtUtc != null && a.ReadAtUtc < alertCutoffUtc)
            .ToListAsync(cancellationToken);
        _context.Alerts.RemoveRange(staleAlerts);

        var sentCutoffUtc = nowUtc.AddDays(-SentEmailRetentionDays);
        var failedCutoffUtc = nowUtc.AddDays(-FailedEmailRetentionDays);
        var staleEmails = await _context.EmailOutbox
            .Where(m => (m.SentAtUtc != null && m.SentAtUtc < sentCutoffUtc) || (m.SentAtUtc == null && m.CreatedAtUtc < failedCutoffUtc))
            .ToListAsync(cancellationToken);
        _context.EmailOutbox.RemoveRange(staleEmails);

        // Reset codes are useless after they expire (15 minutes); keep them a day so a support question can still be answered.
        var codeCutoffUtc = nowUtc.AddDays(-1);
        var staleCodes = await _context.OneTimeCodes
            .Where(c => c.ExpiresAtUtc < codeCutoffUtc)
            .ToListAsync(cancellationToken);
        _context.OneTimeCodes.RemoveRange(staleCodes);

        var readCutoffUtc = nowUtc.AddDays(-ReadNotificationRetentionDays);
        var anyCutoffUtc = nowUtc.AddDays(-AnyNotificationRetentionDays);
        var staleNotifications = await _context.Notifications
            .Where(n => (n.ReadAtUtc != null && n.ReadAtUtc < readCutoffUtc) || n.CreatedAtUtc < anyCutoffUtc)
            .ToListAsync(cancellationToken);
        _context.Notifications.RemoveRange(staleNotifications);

        var tokenCutoffUtc = nowUtc.AddDays(-PushTokenRetentionDays);
        var staleTokens = await _context.PushTokens
            .Where(t => t.LastSeenAtUtc < tokenCutoffUtc)
            .ToListAsync(cancellationToken);
        _context.PushTokens.RemoveRange(staleTokens);

        if (staleHistory.Count > 0 || staleAlerts.Count > 0 || staleEmails.Count > 0 || staleCodes.Count > 0
            || staleNotifications.Count > 0 || staleTokens.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("CleanupJob pruned {HistoryCount} TaskHistory rows and {AlertCount} read alerts.",
            staleHistory.Count, staleAlerts.Count);
    }
}
