using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Tasks.Queries.GetTaskDetail;

public class GetTaskDetailQueryHandler : IRequestHandler<GetTaskDetailQuery, TaskDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public GetTaskDetailQueryHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<TaskDetailDto> Handle(GetTaskDetailQuery request, CancellationToken cancellationToken)
    {
        var task = await _context.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken)
            ?? throw new NotFoundException(nameof(TaskItem), request.TaskId);

        // Only the urgency breakdown is cached: it's the expensive/recomputed-often piece,
        // while the task row itself is already a cheap primary-key lookup.
        var urgencyCacheKey = CacheKeys.TaskUrgency(request.TaskId);
        var breakdown = await _cache.GetAsync<UrgencyScoreBreakdownDto>(urgencyCacheKey, cancellationToken);

        if (breakdown is null)
        {
            var latestLog = await _context.UrgencyScoreLogs
                .AsNoTracking()
                .Where(l => l.TaskItemId == request.TaskId)
                .OrderByDescending(l => l.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestLog is not null)
            {
                breakdown = UrgencyScoreBreakdownDto.FromEntity(latestLog);
                await _cache.SetAsync(urgencyCacheKey, breakdown, CacheKeys.TaskUrgencyTtl, cancellationToken);
            }
        }

        return new TaskDetailDto(TaskDto.FromEntity(task), breakdown);
    }
}
