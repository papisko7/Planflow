using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Authorization;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>The fields of a task that matter for "what changed" e-mails, captured before an update overwrites them.</summary>
public record TaskSnapshot(
    string Title, string? Description, Domain.Enums.TaskStatus Status, Guid? AssignedUserId,
    DateTime? DeadlineUtc, int? ImpactScore, Guid? BlockedByTaskId)
{
    public static TaskSnapshot Of(CalendarItem task) => new(
        task.Title, task.Description, task.Status, task.AssignedUserId, task.DueUtc, task.ImpactScore, task.BlockedByTaskId);
}

/// <summary>
/// Decides who gets which e-mail when a task is created, edited or deleted. The person who made the change is never
/// notified (INotificationService drops the actor), and a person's own per-category / per-team settings are applied there too.
/// </summary>
internal static class TaskNotifications
{
    public static async Task OnCreatedAsync(
        IApplicationDbContext context, INotificationService notifications, CalendarItem task, Guid? actorUserId,
        IReadOnlyList<Guid> participantIds, CancellationToken cancellationToken)
    {
        var teamName = await NotificationLookups.TeamNameAsync(context, task.TeamId, cancellationToken);
        var actorName = await NotificationLookups.UserNameAsync(context, actorUserId, cancellationToken);

        if (task.AssignedUserId is { } assignee)
        {
            await notifications.NotifyAsync(
                NotificationEvent.TaskAssignedToYou, new[] { assignee },
                EmailTemplates.TaskAssigned(task.Title, teamName, actorName, task.DueUtc, task.TaskType),
                task.TeamId, actorUserId, cancellationToken, task.Id);
        }

        await NotifyParticipantsAddedAsync(notifications, task, participantIds, teamName, actorName, actorUserId, cancellationToken);

        // "An admin or owner added a task to my team": only Owner/Admin additions are announced to the whole team,
        // and the assignee already got the more specific mail above.
        var actorRole = await context.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == task.TeamId && m.UserId == actorUserId)
            .Select(m => (TeamRole?)m.Role)
            .FirstOrDefaultAsync(cancellationToken);
        if (actorRole is { } role && RoleDefinitions.HasPermission(role, Permission.EditAnyTask))
        {
            var others = await context.TeamMembers.AsNoTracking()
                .Where(m => m.TeamId == task.TeamId && m.UserId != task.AssignedUserId && !participantIds.Contains(m.UserId))
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);

            await notifications.NotifyAsync(
                NotificationEvent.TeamTaskAdded, others,
                EmailTemplates.TeamTaskAdded(task.Title, teamName, actorName, task.TaskType),
                task.TeamId, actorUserId, cancellationToken, task.Id);
        }
    }

    /// <param name="participantChanges">Who joined / left the item in this save.</param>
    /// <param name="participantIds">Everybody who takes part after the save (the ones who stayed hear about the edit too).</param>
    public static async Task OnUpdatedAsync(
        IApplicationDbContext context, INotificationService notifications, CalendarItem task, TaskSnapshot before,
        Guid? actorUserId, ParticipantChanges participantChanges, IReadOnlyList<Guid> participantIds, CancellationToken cancellationToken)
    {
        var teamName = await NotificationLookups.TeamNameAsync(context, task.TeamId, cancellationToken);
        var actorName = await NotificationLookups.UserNameAsync(context, actorUserId, cancellationToken);

        // Someone who just became the assignee (they were a participant before) is told about the assignment, not about "being removed".
        await NotifyParticipantsAddedAsync(notifications, task, participantChanges.Added, teamName, actorName, actorUserId, cancellationToken);
        var removedForReal = participantChanges.Removed.Where(id => id != task.AssignedUserId).ToList();
        if (removedForReal.Count > 0)
        {
            await notifications.NotifyAsync(
                NotificationEvent.ParticipantRemoved, removedForReal,
                EmailTemplates.ParticipantRemoved(task.Title, teamName, actorName, task.TaskType),
                task.TeamId, actorUserId, cancellationToken);
        }

        if (before.AssignedUserId != task.AssignedUserId)
        {
            if (task.AssignedUserId is { } newAssignee)
            {
                await notifications.NotifyAsync(
                    NotificationEvent.TaskAssignedToYou, new[] { newAssignee },
                    EmailTemplates.TaskAssigned(task.Title, teamName, actorName, task.DueUtc, task.TaskType),
                    task.TeamId, actorUserId, cancellationToken, task.Id);
            }

            if (before.AssignedUserId is { } oldAssignee)
            {
                await notifications.NotifyAsync(
                    NotificationEvent.TaskUnassigned, new[] { oldAssignee },
                    EmailTemplates.TaskUnassigned(task.Title, teamName, actorName, task.TaskType),
                    task.TeamId, actorUserId, cancellationToken);
            }

            await NotifyManagersOfReturnToPoolAsync(context, notifications, task, before, actorUserId, teamName, actorName, cancellationToken);
            return;
        }

        // Same assignee before and after: they hear about edits made by somebody else, and so do the people who were already taking part
        // (someone just added has the invitation mail, which says everything).
        var changes = DescribeChanges(before, task);
        if (changes.Count == 0)
        {
            return;
        }

        var recipients = participantIds.Where(id => !participantChanges.Added.Contains(id)).ToList();
        if (task.AssignedUserId is { } assignee)
        {
            recipients.Add(assignee);
        }

        await notifications.NotifyAsync(
            NotificationEvent.TaskChanged, recipients,
            EmailTemplates.TaskChanged(task.Title, teamName, actorName, changes, task.TaskType),
            task.TeamId, actorUserId, cancellationToken, task.Id);
    }

    // A Member who gives back a task they had taken leaves it with nobody: the team's Owners and Admins are told, so it does not rot unnoticed.
    // (Anything an Owner/Admin did themselves is not announced to Owners/Admins - they know.)
    private static async Task NotifyManagersOfReturnToPoolAsync(
        IApplicationDbContext context, INotificationService notifications, CalendarItem task, TaskSnapshot before, Guid? actorUserId,
        string teamName, string actorName, CancellationToken cancellationToken)
    {
        if (task.AssignedUserId is not null || before.AssignedUserId is null || before.AssignedUserId != actorUserId)
        {
            return;
        }

        var managers = await context.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == task.TeamId && (m.Role == TeamRole.Owner || m.Role == TeamRole.Admin))
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        if (managers.Contains(before.AssignedUserId.Value))
        {
            return;
        }

        await notifications.NotifyAsync(
            NotificationEvent.TaskReturnedToPool, managers,
            EmailTemplates.TaskReturnedToPool(task.Title, teamName, actorName, task.TaskType),
            task.TeamId, actorUserId, cancellationToken, task.Id);
    }

    /// <param name="participantIds">The guests / co-contractors the item had, who hear about its deletion like the assignee.</param>
    public static async Task OnDeletedAsync(
        IApplicationDbContext context, INotificationService notifications, CalendarItem task, Guid? actorUserId,
        IReadOnlyList<Guid> participantIds, CancellationToken cancellationToken)
    {
        var recipients = participantIds.ToList();
        if (task.AssignedUserId is { } assignee)
        {
            recipients.Add(assignee);
        }

        if (recipients.Count == 0)
        {
            return;
        }

        await notifications.NotifyAsync(
            NotificationEvent.TaskDeleted, recipients,
            EmailTemplates.TaskDeleted(
                task.Title,
                await NotificationLookups.TeamNameAsync(context, task.TeamId, cancellationToken),
                await NotificationLookups.UserNameAsync(context, actorUserId, cancellationToken),
                task.TaskType),
            task.TeamId, actorUserId, cancellationToken);
    }

    private static Task NotifyParticipantsAddedAsync(
        INotificationService notifications, CalendarItem task, IReadOnlyList<Guid> addedIds, string teamName, string actorName,
        Guid? actorUserId, CancellationToken cancellationToken) =>
        addedIds.Count == 0
            ? Task.CompletedTask
            : notifications.NotifyAsync(
                NotificationEvent.ParticipantAdded, addedIds,
                EmailTemplates.ParticipantAdded(task.Title, teamName, actorName, task.DueUtc, task.TaskType, task.IsAllDay),
                task.TeamId, actorUserId, cancellationToken, task.Id);

    /// <summary>One short phrase per changed field; an empty list means nothing worth an e-mail changed.</summary>
    internal static List<string> DescribeChanges(TaskSnapshot before, CalendarItem after)
    {
        var changes = new List<string>();

        if (before.Title != after.Title)
        {
            changes.Add($"title \"{before.Title}\" → \"{after.Title}\"");
        }

        if (before.Status != after.Status)
        {
            changes.Add($"status {before.Status} → {after.Status}");
        }

        if (before.DeadlineUtc != after.DeadlineUtc)
        {
            // For an event the tracked moment is its start, not a deadline.
            changes.Add($"{(after.TaskType == TaskType.Event ? "start" : "deadline")} {FormatDeadline(before.DeadlineUtc)} → {FormatDeadline(after.DeadlineUtc)}");
        }

        if (before.ImpactScore != after.ImpactScore)
        {
            changes.Add($"impact {before.ImpactScore?.ToString() ?? "not rated"} → {after.ImpactScore?.ToString() ?? "not rated"}");
        }

        if ((before.Description ?? string.Empty) != (after.Description ?? string.Empty))
        {
            changes.Add("description edited");
        }

        if (before.BlockedByTaskId != after.BlockedByTaskId)
        {
            changes.Add(after.BlockedByTaskId is null ? "no longer blocked by another task" : "now blocked by another task");
        }

        return changes;
    }

    private static string FormatDeadline(DateTime? deadlineUtc) =>
        deadlineUtc is null ? "none" : deadlineUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") + " UTC";
}
