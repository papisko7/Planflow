using MediatR;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Tasks.Commands.CreateTask;

public class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, TaskDto>
{
    private readonly IApplicationDbContext _context;

    public CreateTaskCommandHandler(IApplicationDbContext context)
    {
        _context = context;
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
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, DateTime.UtcNow);
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

        return TaskDto.FromEntity(task);
    }
}
