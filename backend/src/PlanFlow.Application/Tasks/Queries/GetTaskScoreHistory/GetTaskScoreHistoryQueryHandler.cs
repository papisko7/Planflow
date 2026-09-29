using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Tasks.Queries.GetTaskScoreHistory;

public class GetTaskScoreHistoryQueryHandler : IRequestHandler<GetTaskScoreHistoryQuery, IReadOnlyList<UrgencyScoreBreakdownDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTaskScoreHistoryQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UrgencyScoreBreakdownDto>> Handle(GetTaskScoreHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!await _context.Tasks.AnyAsync(t => t.Id == request.TaskId, cancellationToken))
        {
            throw new NotFoundException(nameof(TaskItem), request.TaskId);
        }

        var logs = await _context.UrgencyScoreLogs
            .AsNoTracking()
            .Where(l => l.TaskItemId == request.TaskId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return logs.Select(UrgencyScoreBreakdownDto.FromEntity).ToList();
    }
}
