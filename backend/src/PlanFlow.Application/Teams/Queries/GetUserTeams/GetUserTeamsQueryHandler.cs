using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Teams.Common;

namespace PlanFlow.Application.Teams.Queries.GetUserTeams;

public class GetUserTeamsQueryHandler : IRequestHandler<GetUserTeamsQuery, IReadOnlyList<TeamDto>>
{
    private readonly IApplicationDbContext _context;

    public GetUserTeamsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TeamDto>> Handle(GetUserTeamsQuery request, CancellationToken cancellationToken)
    {
        // Project member count as a subquery count rather than Include()-ing the full Members
        // collection, since only the count is needed here.
        var teams = await _context.TeamMembers
            .AsNoTracking()
            .Where(m => m.UserId == request.UserId)
            .Select(m => new
            {
                m.Team.Id,
                m.Team.Name,
                m.Team.Description,
                MemberCount = m.Team.Members.Count
            })
            .ToListAsync(cancellationToken);

        return teams.Select(t => new TeamDto(t.Id, t.Name, t.Description, t.MemberCount)).ToList();
    }
}
