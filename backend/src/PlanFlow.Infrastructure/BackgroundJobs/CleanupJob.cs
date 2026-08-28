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

        if (staleHistory.Count > 0 || staleAlerts.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("CleanupJob pruned {HistoryCount} TaskHistory rows and {AlertCount} read alerts.",
            staleHistory.Count, staleAlerts.Count);
    }
}
