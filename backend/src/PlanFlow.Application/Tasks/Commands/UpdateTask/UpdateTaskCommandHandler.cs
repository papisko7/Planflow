using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using ScoreTriggerSource = PlanFlow.Domain.Enums.ScoreTriggerSource;

namespace PlanFlow.Application.Tasks.Commands.UpdateTask;

public class UpdateTaskCommandHandler : IRequestHandler<UpdateTaskCommand, TaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public UpdateTaskCommandHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<TaskDto> Handle(UpdateTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _context.Tasks
            .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken)
            ?? throw new NotFoundException(nameof(TaskItem), request.TaskId);

        var previousStatus = task.Status;
        var previousAssignedUserId = task.AssignedUserId;

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

        var blockedTaskCount = await _context.Tasks
            .CountAsync(t => t.BlockedByTaskId == task.Id, cancellationToken);

        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount, nowUtc, ScoreTriggerSource.ManualUpdate);
        task.CurrentUrgencyScore = scoreLog.FinalScore;
        _context.UrgencyScoreLogs.Add(scoreLog);

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

        await _context.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync(CacheKeys.TeamTasks(task.TeamId), cancellationToken);
        await _cache.RemoveAsync(CacheKeys.TaskUrgency(task.Id), cancellationToken);

        return TaskDto.FromEntity(task);
    }
}
