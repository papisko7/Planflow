using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Raises an <see cref="AlertType.UrgencySpike"/> alert for every assigned task whose
/// urgency score (kept fresh by <see cref="PrioritizationJob"/>) crosses <see cref="UrgencyThreshold"/>.
/// Runs every minute so alerts fire close to the moment a deadline pushes a task over the line.
/// </summary>
public class AlertingJob : IJob
{
    /// <summary>Urgency score (0-1) at/above which a task is considered alert-worthy.</summary>
    private const double UrgencyThreshold = 0.8;

    /// <summary>Suppresses a duplicate alert for the same task within this window (anti-spam).</summary>
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromHours(6);

    /// <summary>The e-mail is sent far less often than the in-app alert: a task that stays urgent for days must not mail its assignee every 6 hours.</summary>
    private static readonly TimeSpan EmailDedupeWindow = TimeSpan.FromHours(24);

    private readonly IApplicationDbContext _context;
    private readonly INotificationService _notifications;
    private readonly ILogger<AlertingJob> _logger;

    public AlertingJob(IApplicationDbContext context, INotificationService notifications, ILogger<AlertingJob> logger)
    {
        _context = context;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var nowUtc = DateTime.UtcNow;
        var dedupeCutoffUtc = nowUtc - DedupeWindow;

        var candidateTasks = await _context.CalendarItems
            .Where(t => t.Status != TaskStatus.Done && t.Status != TaskStatus.Cancelled)
            .Where(t => t.AssignedUserId != null)
            .Where(t => t.CurrentUrgencyScore >= UrgencyThreshold)
            .Select(t => new { t.Id, t.TeamId, t.AssignedUserId, t.Title, t.TaskType, DeadlineUtc =t.DeadlineUtc ?? t.StartUtc, t.CurrentUrgencyScore })
            .ToListAsync(cancellationToken);

        if (candidateTasks.Count == 0)
        {
            return;
        }

        var candidateTaskIds = candidateTasks.Select(t => t.Id).ToList();
        var emailCutoffUtc = nowUtc - EmailDedupeWindow;
        var recentAlerts = await _context.Alerts
            .Where(a => a.Type == AlertType.UrgencySpike)
            .Where(a => a.CalendarItemId != null && candidateTaskIds.Contains(a.CalendarItemId.Value))
            .Where(a => a.CreatedAtUtc >= emailCutoffUtc)
            .Select(a => new { TaskId = a.CalendarItemId!.Value, a.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var recentlyAlertedSet = recentAlerts.Where(a => a.CreatedAtUtc >= dedupeCutoffUtc).Select(a => a.TaskId).ToHashSet();
        var recentlyEmailedSet = recentAlerts.Select(a => a.TaskId).ToHashSet();
        var newAlerts = 0;

        foreach (var task in candidateTasks)
        {
            if (recentlyAlertedSet.Contains(task.Id))
            {
                continue;
            }

            _context.Alerts.Add(new Alert
            {
                UserId = task.AssignedUserId!.Value,
                CalendarItemId = task.Id,
                Type = AlertType.UrgencySpike,
                Message = $"{EmailTemplates.Capitalized(task.TaskType)} \"{task.Title}\" urgency reached {task.CurrentUrgencyScore:P0}."
            });
            newAlerts++;

            if (!recentlyEmailedSet.Contains(task.Id))
            {
                // The assignee's own settings (master switch, "urgent tasks" switch, team mute) are applied by the service.
                await _notifications.NotifyAsync(
                    NotificationEvent.TaskUrgent,
                    new[] { task.AssignedUserId!.Value },
                    EmailTemplates.TaskUrgent(task.Title, task.DeadlineUtc, task.CurrentUrgencyScore, task.TaskType),
                    task.TeamId,
                    cancellationToken: cancellationToken,
                    taskId: task.Id);
            }
        }

        if (newAlerts > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("AlertingJob raised {NewAlerts} urgency alerts ({CandidateCount} candidates, {DedupedCount} deduped).",
            newAlerts, candidateTasks.Count, recentlyAlertedSet.Count);
    }
}
