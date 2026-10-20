using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Calendar.Common;
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
            .Include(t => t.RecurrenceSeries)
            .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken)
            ?? throw new NotFoundException(nameof(CalendarItem), request.TaskId);

        // Who may change the assignee is decided here (not in the controller), so every caller plays by the same rules. It comes first:
        // being refused is answered before the request is checked for mistakes.
        var outcome = TaskAssignmentPolicy.Decide(
            request.UpdatedByUserId, request.CallerIsManager, task.AssignedUserId, task.AssignedByUserId, task.IsAssignmentLocked,
            request.AssignedUserId);

        await TaskAssignment.EnsureAssigneeInTeamAsync(_context, task.TeamId, request.AssignedUserId, cancellationToken);
        await TaskDependencies.EnsureValidBlockerAsync(_context, task.TeamId, task.Id, request.BlockedByTaskId, cancellationToken);

        var previousStatus = task.Status;
        var previousAssignedUserId = task.AssignedUserId;
        var before = TaskSnapshot.Of(task); // for the "your task was changed" e-mail
        var seriesBefore = SeriesSnapshot.Of(task); // for the series pop-up: did this occurrence change on its own?
        var originalDue = task.DueUtc;
        var originalSeries = task.RecurrenceSeries;
        var mirrorBefore = GoogleMirror.Capture(task); // for an imported item: what changed here is sent to Google by the next sync

        task.AssignedUserId = request.AssignedUserId;
        task.AssignedByUserId = outcome.AssignedByUserId;
        task.IsAssignmentLocked = outcome.IsLocked;
        task.Title = request.Title;
        if (task.IsGoogleTask)
        {
            ApplyGoogleTaskPlanning(task, request);
        }
        else
        {
            // An imported event can be edited like any other, except for its kind (the event / template Google says it is) and its repeat
            // rule (Google's): those are kept whatever the request says. The next sync sends the edit to Google (last write wins).
            if (task.IsExternal)
            {
                request = request with { TaskType = task.TaskType, EventTemplate = task.EventTemplate, IsRecurring = false };
            }
            TaskScheduleMapper.Apply(task, request);
        }
        // A Task is Backlog/InProgress/Completed/Cancelled, an Event Confirmed/Tentative/Cancelled. When the type changed (or an
        // older app sent the other type's status) the status falls back to the new type's default instead of failing the save.
        task.Status = TaskStatusRules.Normalize(task.TaskType, request.Status);
        task.ItemListId = await TaskItemLists.ResolveAsync(_context, task.TeamId, task.TaskType, request.ItemListId, cancellationToken);
        task.ImpactScore = request.ImpactScore;
        task.BlockedByTaskId = request.BlockedByTaskId;
        task.IsDataIncomplete = false; // the user has looked at this item and saved it: the "please complete" reminder is done
        task.ManualUrgencyOverride = request.ManualUrgencyOverride;
        task.ManualUrgencyOverrideExpiresAtUtc = request.ManualUrgencyOverrideExpiresAtUtc;

        // Alerts belong to the person (for an imported event Google's list is only the starting point). A caller that sends no list
        // (a quick action such as "done", an older app) leaves them as they are.
        IReadOnlyList<ReminderInput> alerts;
        if (request.Reminders is not null)
        {
            alerts = TaskReminders.Resolve(request.Reminders, task);
            TaskReminders.Replace(_context, task, alerts);
        }
        else
        {
            alerts = AlertsAndPeople.Of(task).Reminders;
        }

        // Guests / co-contractors: the same "no list = no change". The assignee is never also a participant, so handing the item to one of
        // its participants quietly moves them from the list to the assignee.
        var participantIds = request.ParticipantUserIds is null
            ? task.Participants.Select(p => p.UserId).Where(id => id != task.AssignedUserId).ToList()
            : await TaskParticipants.ResolveAsync(_context, task, request.ParticipantUserIds, cancellationToken);
        var participantChanges = TaskParticipants.Replace(_context, task, participantIds);

        var nowUtc = DateTime.UtcNow;
        task.UpdatedAtUtc = nowUtc;
        GoogleMirror.Track(task, mirrorBefore, GoogleMirror.Capture(task, alerts.Select(a => a.ToEntity().Key)), nowUtc);

        // The time zone matters when an edit is passed on to other occurrences: their time of day is kept in the person's zone.
        var passesOnToSeries = task.IsExternal ? task.SeriesId is not null && request.Scope != SeriesScope.This : request.IsRecurring;
        var zoneId = passesOnToSeries ? await TaskSeries.ZoneOfAsync(_context, request.UpdatedByUserId, cancellationToken) : null;
        var edit = new SeriesEdit(
            request, task, originalSeries, seriesBefore, originalDue, previousAssignedUserId != task.AssignedUserId, participantIds,
            new AlertsAndPeople(alerts, participantIds), zoneId);
        var seriesResult = new SeriesEditResult([], null);
        if (task.IsExternal)
        {
            await SeriesEditing.ApplyImportedAsync(_context, edit, nowUtc, cancellationToken);
        }
        else
        {
            seriesResult = await SeriesEditing.ApplyAsync(_context, edit, nowUtc, cancellationToken);
        }

        // Occurrences may have been removed or created: save them first, so the rescoring below sees the team as it now is.
        if (request.Scope != SeriesScope.This || seriesResult.Created.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

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

        return TaskDto.FromEntity(task, task.IsExternal ? null : seriesResult.Series) with { ParticipantUserIds = participantIds };
    }

    /// <summary>
    /// The fields of a Google task, all editable: notes, effort, the planned start (Google Tasks has none) and the deadline, both kept as
    /// whole days. A task with no due day in Google got a placeholder deadline ("tomorrow"); saving confirms it, so the placeholder flag goes.
    /// </summary>
    private static void ApplyGoogleTaskPlanning(CalendarItem task, UpdateTaskCommand request)
    {
        task.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description;
        task.EstimatedEffortHours = request.EstimatedEffortHours;
        task.StartUtc = request.StartUtc is { } start ? WholeDay(start) : null;
        if (request.DeadlineUtc is { } deadline)
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
