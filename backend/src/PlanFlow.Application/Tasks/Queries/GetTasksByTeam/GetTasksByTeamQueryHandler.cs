using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Queries.GetTasksByTeam;

public class GetTasksByTeamQueryHandler : IRequestHandler<GetTasksByTeamQuery, IReadOnlyList<TaskDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTasksByTeamQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TaskDto>> Handle(GetTasksByTeamQuery request, CancellationToken cancellationToken)
    {
        // Materialize entities first, then map to TaskDto in memory: EF Core cannot translate
        // a call to a factory method (TaskDto.FromEntity) into SQL inside a Select projection.
        var tasks = await _context.Tasks
            .AsNoTracking()
            .Where(t => t.TeamId == request.TeamId)
            .OrderByDescending(t => t.CurrentUrgencyScore)
            .ToListAsync(cancellationToken);

        return tasks.Select(TaskDto.FromEntity).ToList();
    }
}
