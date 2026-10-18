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
            Description = request.Description,
            ImpactScore = request.ImpactScore,
            BlockedByTaskId = request.BlockedByTaskId,
            UseAiEnhancement = request.UseAiEnhancement
        };
        TaskScheduleMapper.Apply(task, request);
        task.Status = TaskStatusRules.DefaultFor(task.TaskType); // a Task starts in Backlog, an Event is Confirmed

        _context.CalendarItems.Add(task);

        // A new task can already be blocked by an existing one, and that blocker may now inherit urgency from it.
        var changedTaskIds = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            _context, task.TeamId, DateTime.UtcNow, ScoreTriggerSource.ManualCreate, task, cancellationToken);

        _context.TaskHistories.Add(new TaskHistory
        {
            CalendarItemId = task.Id,
            ChangedByUserId = request.CreatedByUserId,
            ChangeType = TaskChangeType.Created
        });

        await TaskNotifications.OnCreatedAsync(_context, _notifications, task, request.CreatedByUserId, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await TeamUrgencyRecalculator.InvalidateCachesAsync(_cache, task.TeamId, changedTaskIds, cancellationToken);

        return TaskDto.FromEntity(task);
    }
}
