using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Interfaces;
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

    private readonly IApplicationDbContext _context;
    private readonly ILogger<AlertingJob> _logger;

    public AlertingJob(IApplicationDbContext context, ILogger<AlertingJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var nowUtc = DateTime.UtcNow;
        var dedupeCutoffUtc = nowUtc - DedupeWindow;

        var candidateTasks = await _context.Tasks
            .Where(t => t.Status != TaskStatus.Done && t.Status != TaskStatus.Cancelled)
            .Where(t => t.AssignedUserId != null)
            .Where(t => t.CurrentUrgencyScore >= UrgencyThreshold)
            .Select(t => new { t.Id, t.AssignedUserId, t.Title, t.CurrentUrgencyScore })
            .ToListAsync(cancellationToken);

        if (candidateTasks.Count == 0)
        {
            return;
        }

        var candidateTaskIds = candidateTasks.Select(t => t.Id).ToList();
        var recentlyAlertedTaskIds = await _context.Alerts
            .Where(a => a.Type == AlertType.UrgencySpike)
            .Where(a => a.TaskItemId != null && candidateTaskIds.Contains(a.TaskItemId.Value))
            .Where(a => a.CreatedAtUtc >= dedupeCutoffUtc)
            .Select(a => a.TaskItemId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var recentlyAlertedSet = recentlyAlertedTaskIds.ToHashSet();
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
                TaskItemId = task.Id,
                Type = AlertType.UrgencySpike,
                Message = $"Task \"{task.Title}\" urgency reached {task.CurrentUrgencyScore:P0}."
            });
            newAlerts++;
        }

        if (newAlerts > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("AlertingJob raised {NewAlerts} urgency alerts ({CandidateCount} candidates, {DedupedCount} deduped).",
            newAlerts, candidateTasks.Count, recentlyAlertedSet.Count);
    }
}
