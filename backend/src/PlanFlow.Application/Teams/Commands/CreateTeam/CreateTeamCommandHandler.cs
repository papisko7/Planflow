using MediatR;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Teams.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Teams.Commands.CreateTeam;

public class CreateTeamCommandHandler : IRequestHandler<CreateTeamCommand, TeamDto>
{
    private readonly IApplicationDbContext _context;

    public CreateTeamCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TeamDto> Handle(CreateTeamCommand request, CancellationToken cancellationToken)
    {
        var team = new Team
        {
            Name = request.Name,
            Description = request.Description
        };

        var ownerMembership = new TeamMember
        {
            TeamId = team.Id,
            UserId = request.OwnerUserId,
            Role = TeamRole.Owner
        };

        _context.Teams.Add(team);
        _context.TeamMembers.Add(ownerMembership);

        await _context.SaveChangesAsync(cancellationToken);

        return new TeamDto(team.Id, team.Name, team.Description, MemberCount: 1);
    }
}
