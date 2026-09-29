using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Teams.Common;

namespace PlanFlow.Application.Teams.Queries.GetTeamMembers;

public class GetTeamMembersQueryHandler : IRequestHandler<GetTeamMembersQuery, IReadOnlyList<TeamMemberDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTeamMembersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TeamMemberDto>> Handle(GetTeamMembersQuery request, CancellationToken cancellationToken)
    {
        // Only members of a team may see its roster — stops outsiders enumerating other teams' users.
        var isMember = await _context.TeamMembers
            .AnyAsync(m => m.TeamId == request.TeamId && m.UserId == request.RequestingUserId, cancellationToken);

        if (!isMember)
        {
            throw new ForbiddenAccessException("You are not a member of this team.");
        }

        var members = await _context.TeamMembers
            .AsNoTracking()
            .Where(m => m.TeamId == request.TeamId)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => new TeamMemberDto(m.Id, m.UserId, m.User.DisplayName, m.Role, m.JoinedAtUtc))
            .ToListAsync(cancellationToken);

        return members;
    }
}
