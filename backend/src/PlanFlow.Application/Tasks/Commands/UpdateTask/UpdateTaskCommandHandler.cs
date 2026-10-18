using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using ScoreTriggerSource = PlanFlow.Domain.Enums.ScoreTriggerSource;

namespace PlanFlow.Application.Tasks.Commands.UpdateTask;

public class UpdateTaskCommandHandler : IRequestHandler<UpdateTaskCommand, TaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly INotificationService _notifications;

    public UpdateTaskCommandHandler(IApplicationDbContext context, ICacheService cache, INotificationService notifications)
    {
        _context = context;
        _cache = cache;
        _notifications = notifications;
    }

    public async Task<TaskDto> Handle(UpdateTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _context.CalendarItems
            .Include(t => t.Reminders)
            .Include(t => t.Participants)
            .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken)
            ?? throw new NotFoundException(nameof(CalendarItem), request.TaskId);

        await TaskAssignment.EnsureAssigneeInTeamAsync(_context, task.TeamId, request.AssignedUserId, cancellationToken);
        await TaskDependencies.EnsureValidBlockerAsync(_context, task.TeamId, task.Id, request.BlockedByTaskId, cancellationToken);

        var previousStatus = task.Status;
        var previousAssignedUserId = task.AssignedUserId;
        var before = TaskSnapshot.Of(task); // for the "your task was changed" e-mail

        task.AssignedUserId = request.AssignedUserId;
        if (task.IsExternal)
        {
            // Google owns the title, time, place, description, colour and template of an imported item (the next sync would overwrite
            // them), so only PlanFlow's own fields are taken from the request: effort here, impact / status / assignee below.
            task.EstimatedEffortHours = request.EstimatedEffortHours;
            if (task.IsGoogleTask)
            {
                ApplyGoogleTaskPlanning(task, request);
            }
        }
        else
        {
            task.Title = request.Title;
            TaskScheduleMapper.Apply(task, request);
        }
        // A Task is Backlog/InProgress/Completed/Cancelled, an Event Confirmed/Tentative/Cancelled. When the type changed (or an
        // older app sent the other type's status) the status falls back to the new type's default instead of failing the save.
        task.Status = TaskStatusRules.Normalize(task.TaskType, request.Status);
        task.ItemListId = await TaskItemLists.ResolveAsync(_context, task.TeamId, task.TaskType, request.ItemListId, cancellationToken);
        task.UseAiEnhancement = request.UseAiEnhancement;
        task.ImpactScore = request.ImpactScore;
        task.BlockedByTaskId = request.BlockedByTaskId;
        task.IsDataIncomplete = false; // the user has looked at this item and saved it: the "please complete" reminder is done
        task.ManualUrgencyOverride = request.ManualUrgencyOverride;
        task.ManualUrgencyOverrideExpiresAtUtc = request.ManualUrgencyOverrideExpiresAtUtc;

        // Alerts: an imported event's belong to Google (the next sync would overwrite ours), everything else to the person. A caller that
        // sends no list (a quick action such as "done", an older app) leaves them as they are.
        if (request.Reminders is not null && !(task.IsExternal && !task.IsGoogleTask))
        {
            TaskReminders.Replace(_context, task, TaskReminders.Resolve(request.Reminders, task));
        }

        // Guests / co-contractors: the same "no list = no change". The assignee is never also a participant, so handing the item to one of
        // its participants quietly moves them from the list to the assignee.
        var participantIds = request.ParticipantUserIds is null
            ? task.Participants.Select(p => p.UserId).Where(id => id != task.AssignedUserId).ToList()
            : await TaskParticipants.ResolveAsync(_context, task, request.ParticipantUserIds, cancellationToken);
        var participantChanges = TaskParticipants.Replace(_context, task, participantIds);

        var nowUtc = DateTime.UtcNow;
        task.UpdatedAtUtc = nowUtc;

        // Editing one task can change others (its blocker/blocked neighbours), so the whole team is rescored.
        var changedTaskIds = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            _context, task.TeamId, nowUtc, ScoreTriggerSource.ManualUpdate, task, cancellationToken);

        _context.TaskHistories.Add(new TaskHistory
        {
            CalendarItemId = task.Id,
            ChangedByUserId = request.UpdatedByUserId,
            ChangeType = previousStatus != task.Status
                ? TaskChangeType.StatusChanged
                : previousAssignedUserId != task.AssignedUserId
                    ? TaskChangeType.Reassigned
                    : TaskChangeType.FieldUpdated
        });

        await TaskNotifications.OnUpdatedAsync(
            _context, _notifications, task, before, request.UpdatedByUserId, participantChanges, participantIds, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        await TeamUrgencyRecalculator.InvalidateCachesAsync(_cache, task.TeamId, changedTaskIds.Append(task.Id), cancellationToken);

        return TaskDto.FromEntity(task) with { ParticipantUserIds = participantIds };
    }

    /// <summary>
    /// What a Google task lets PlanFlow decide. Google Tasks has no planned start, so it is the user's (kept as a whole day, like the deadline).
    /// A task with no due day in Google got a placeholder deadline ("tomorrow"); until the user saves it, that deadline is theirs to set.
    /// Saving confirms the item, so the placeholder flag goes either way.
    /// </summary>
    private static void ApplyGoogleTaskPlanning(CalendarItem task, UpdateTaskCommand request)
    {
        task.StartUtc = request.StartUtc is { } start ? WholeDay(start) : null;
        if (task.IsDeadlineDefaulted && request.DeadlineUtc is { } deadline)
        {
            task.DeadlineUtc = WholeDay(deadline);
        }
        task.IsDeadlineDefaulted = false;

        // A task never starts after its deadline (the validator checks the request; this guards the stored deadline, which wins).
        if (task.StartUtc > task.DeadlineUtc)
        {
            task.StartUtc = task.DeadlineUtc;
        }
    }

    private static DateTime WholeDay(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
