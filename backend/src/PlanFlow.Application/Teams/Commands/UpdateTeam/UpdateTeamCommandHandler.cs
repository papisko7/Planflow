using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Teams.Common;
using PlanFlow.Domain.Authorization;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Teams.Commands.UpdateTeam;

public class UpdateTeamCommandHandler : IRequestHandler<UpdateTeamCommand, TeamDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateTeamCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TeamDto> Handle(UpdateTeamCommand request, CancellationToken cancellationToken)
    {
        // Permission comes from the caller's real membership row for THIS team, not from a JWT claim.
        var requesting = await _context.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == request.TeamId && m.UserId == request.RequestingUserId, cancellationToken)
            ?? throw new NotFoundException(nameof(Team), request.TeamId);

        if (!RoleDefinitions.HasPermission(requesting.Role, Permission.EditTeam))
        {
            throw new ForbiddenAccessException("You do not have permission to edit this team.");
        }

        var team = await _context.Teams.FirstOrDefaultAsync(t => t.Id == request.TeamId, cancellationToken)
            ?? throw new NotFoundException(nameof(Team), request.TeamId);

        if (team.IsPersonal)
        {
            throw new ForbiddenAccessException("A personal workspace cannot be edited.");
        }

        team.Name = request.Name.Trim();
        team.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        team.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var memberCount = await _context.TeamMembers.CountAsync(m => m.TeamId == team.Id, cancellationToken);
        return new TeamDto(team.Id, team.Name, team.Description, memberCount, team.IsPersonal);
    }
}
