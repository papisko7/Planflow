using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
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
        var task = await _context.Tasks
            .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken)
            ?? throw new NotFoundException(nameof(TaskItem), request.TaskId);

        await TaskAssignment.EnsureAssigneeInTeamAsync(_context, task.TeamId, request.AssignedUserId, cancellationToken);
        await TaskDependencies.EnsureValidBlockerAsync(_context, task.TeamId, task.Id, request.BlockedByTaskId, cancellationToken);

        var previousStatus = task.Status;
        var previousAssignedUserId = task.AssignedUserId;
        var before = TaskSnapshot.Of(task); // for the "your task was changed" e-mail

        task.Title = request.Title;
        task.Description = request.Description;
        task.Status = request.Status;
        task.AssignedUserId = request.AssignedUserId;
        task.DeadlineUtc = request.DeadlineUtc;
        task.ImpactScore = request.ImpactScore;
        task.BlockedByTaskId = request.BlockedByTaskId;
        task.ManualUrgencyOverride = request.ManualUrgencyOverride;
        task.ManualUrgencyOverrideExpiresAtUtc = request.ManualUrgencyOverrideExpiresAtUtc;

        var nowUtc = DateTime.UtcNow;
        task.UpdatedAtUtc = nowUtc;

        // Editing one task can change others (its blocker/blocked neighbours), so the whole team is rescored.
        var changedTaskIds = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            _context, task.TeamId, nowUtc, ScoreTriggerSource.ManualUpdate, task, cancellationToken);

        _context.TaskHistories.Add(new TaskHistory
        {
            TaskItemId = task.Id,
            ChangedByUserId = request.UpdatedByUserId,
            ChangeType = previousStatus != task.Status
                ? TaskChangeType.StatusChanged
                : previousAssignedUserId != task.AssignedUserId
                    ? TaskChangeType.Reassigned
                    : TaskChangeType.FieldUpdated
        });

        await TaskNotifications.OnUpdatedAsync(_context, _notifications, task, before, request.UpdatedByUserId, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        await TeamUrgencyRecalculator.InvalidateCachesAsync(_cache, task.TeamId, changedTaskIds.Append(task.Id), cancellationToken);

        return TaskDto.FromEntity(task);
    }
}
