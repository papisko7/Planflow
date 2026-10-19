using MediatR;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using ScoreTriggerSource = PlanFlow.Domain.Enums.ScoreTriggerSource;

namespace PlanFlow.Application.Tasks.Commands.CreateTask;

public class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, TaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly INotificationService _notifications;

    public CreateTaskCommandHandler(IApplicationDbContext context, ICacheService cache, INotificationService notifications)
    {
        _context = context;
        _cache = cache;
        _notifications = notifications;
    }

    public async Task<TaskDto> Handle(CreateTaskCommand request, CancellationToken cancellationToken)
    {
        await TaskAssignment.EnsureAssigneeInTeamAsync(_context, request.TeamId, request.AssignedUserId, cancellationToken);
        await TaskDependencies.EnsureValidBlockerAsync(_context, request.TeamId, null, request.BlockedByTaskId, cancellationToken);

        var task = new CalendarItem
        {
            TeamId = request.TeamId,
            AssignedUserId = request.AssignedUserId,
            Title = request.Title,
            ImpactScore = request.ImpactScore,
            BlockedByTaskId = request.BlockedByTaskId,
            UseAiEnhancement = request.UseAiEnhancement
        };
        var outcome = TaskAssignmentPolicy.ForNewItem(request.CreatedByUserId, request.CreatorIsManager, request.AssignedUserId);
        task.AssignedByUserId = outcome.AssignedByUserId;
        task.IsAssignmentLocked = outcome.IsLocked;
        TaskScheduleMapper.Apply(task, request);
        task.Status = TaskStatusRules.DefaultFor(task.TaskType); // a Task starts in Backlog, an Event is Confirmed
        task.ItemListId = await TaskItemLists.ResolveAsync(_context, request.TeamId, task.TaskType, request.ItemListId, cancellationToken);

        _context.CalendarItems.Add(task);

        // Alerts and guests are checked against the item as it will be stored (type, template, all-day and the dates are set above).
        var reminders = TaskReminders.Resolve(request.Reminders ?? [], task);
        TaskReminders.Replace(_context, task, reminders);
        var participantIds = await TaskParticipants.ResolveAsync(_context, task, request.ParticipantUserIds ?? [], cancellationToken);
        TaskParticipants.Replace(_context, task, participantIds);

        // A repeating item is the first occurrence of a new series; an event also gets its occurrences for the next 90 days right away.
        var nowUtc = DateTime.UtcNow;
        RecurrenceSeries? series = null;
        IReadOnlyList<CalendarItem> occurrences = [];
        if (request.IsRecurring && task.DueUtc is not null)
        {
            var zoneId = await TaskSeries.ZoneOfAsync(_context, request.CreatedByUserId, cancellationToken);
            series = TaskSeries.Start(_context, task, request, zoneId);
            if (task.TaskType == TaskType.Event)
            {
                occurrences = TaskSeries.CreateAhead(
                    _context, series, task, new AlertsAndPeople(reminders, participantIds), nowUtc, new HashSet<DateTime> { task.DueUtc.Value });
            }
        }

        // A new task can already be blocked by an existing one, and that blocker may now inherit urgency from it.
        var changedTaskIds = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            _context, task.TeamId, nowUtc, ScoreTriggerSource.ManualCreate, task, cancellationToken);
        if (occurrences.Count > 0)
        {
            changedTaskIds = changedTaskIds.Concat(TeamUrgencyRecalculator.Apply(_context, occurrences, nowUtc, ScoreTriggerSource.ManualCreate)).ToList();
        }

        _context.TaskHistories.Add(new TaskHistory
        {
            CalendarItemId = task.Id,
            ChangedByUserId = request.CreatedByUserId,
            ChangeType = TaskChangeType.Created
        });

        await TaskNotifications.OnCreatedAsync(_context, _notifications, task, request.CreatedByUserId, participantIds, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await TeamUrgencyRecalculator.InvalidateCachesAsync(_cache, task.TeamId, changedTaskIds, cancellationToken);

        return TaskDto.FromEntity(task, series) with { ParticipantUserIds = participantIds };
    }
}
