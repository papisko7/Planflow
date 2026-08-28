using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Teams.Common;
using PlanFlow.Domain.Authorization;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Teams.Commands.AddTeamMember;

public class AddTeamMemberCommandHandler : IRequestHandler<AddTeamMemberCommand, TeamMemberDto>
{
    private readonly IApplicationDbContext _context;

    public AddTeamMemberCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TeamMemberDto> Handle(AddTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var requestingMembership = await _context.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.TeamId == request.TeamId && m.UserId == request.RequestingUserId,
                cancellationToken)
            ?? throw new NotFoundException(nameof(TeamMember), request.RequestingUserId);

        if (!RoleDefinitions.HasPermission(requestingMembership.Role, Permission.ManageMembers))
        {
            throw new ForbiddenAccessException("You do not have permission to manage this team's members.");
        }

        var alreadyMember = await _context.TeamMembers
            .AnyAsync(m => m.TeamId == request.TeamId && m.UserId == request.NewMemberUserId, cancellationToken);

        if (alreadyMember)
        {
            throw new ConflictException("This user is already a member of the team.");
        }

        var membership = new TeamMember
        {
            TeamId = request.TeamId,
            UserId = request.NewMemberUserId,
            Role = request.Role
        };

        _context.TeamMembers.Add(membership);
        await _context.SaveChangesAsync(cancellationToken);

        var user = await _context.Users
            .AsNoTracking()
            .FirstAsync(u => u.Id == request.NewMemberUserId, cancellationToken);

        return new TeamMemberDto(membership.Id, membership.UserId, user.DisplayName, membership.Role, membership.JoinedAtUtc);
    }
}
