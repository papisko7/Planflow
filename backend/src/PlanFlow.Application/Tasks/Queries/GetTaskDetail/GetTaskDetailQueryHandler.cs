using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Tasks.Queries.GetTaskDetail;

public class GetTaskDetailQueryHandler : IRequestHandler<GetTaskDetailQuery, TaskDetailDto>
{
    private readonly IApplicationDbContext _context;

    public GetTaskDetailQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TaskDetailDto> Handle(GetTaskDetailQuery request, CancellationToken cancellationToken)
    {
        var task = await _context.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken)
            ?? throw new NotFoundException(nameof(TaskItem), request.TaskId);

        var latestLog = await _context.UrgencyScoreLogs
            .AsNoTracking()
            .Where(l => l.TaskItemId == request.TaskId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new TaskDetailDto(
            TaskDto.FromEntity(task),
            latestLog is null ? null : UrgencyScoreBreakdownDto.FromEntity(latestLog));
    }
}
