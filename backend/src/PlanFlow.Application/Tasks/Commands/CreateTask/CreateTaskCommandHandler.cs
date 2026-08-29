using MediatR;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using ScoreTriggerSource = PlanFlow.Domain.Enums.ScoreTriggerSource;

namespace PlanFlow.Application.Tasks.Commands.CreateTask;

public class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, TaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public CreateTaskCommandHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<TaskDto> Handle(CreateTaskCommand request, CancellationToken cancellationToken)
    {
        var task = new TaskItem
        {
            TeamId = request.TeamId,
            AssignedUserId = request.AssignedUserId,
            Title = request.Title,
            Description = request.Description,
            DeadlineUtc = request.DeadlineUtc,
            ImpactScore = request.ImpactScore,
            BlockedByTaskId = request.BlockedByTaskId
        };

        // A brand-new task can't yet block anything, so the blocking component starts at 0.
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, DateTime.UtcNow, ScoreTriggerSource.ManualCreate);
        task.CurrentUrgencyScore = scoreLog.FinalScore;

        _context.Tasks.Add(task);
        _context.UrgencyScoreLogs.Add(scoreLog);
        _context.TaskHistories.Add(new TaskHistory
        {
            TaskItemId = task.Id,
            ChangedByUserId = request.CreatedByUserId,
            ChangeType = TaskChangeType.Created
        });

        await _context.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.TeamTasks(task.TeamId), cancellationToken);

        return TaskDto.FromEntity(task);
    }
}
