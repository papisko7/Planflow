using MediatR;
using PlanFlow.Application.Teams.Common;

namespace PlanFlow.Application.Teams.Commands.AddTeamMember;

/// <summary>Adds a user to a team with a given role; only a member with ManageMembers permission may do this.</summary>
public record AddTeamMemberCommand(
    Guid TeamId,
    Guid RequestingUserId,
    Guid NewMemberUserId,
    Domain.Enums.TeamRole Role) : IRequest<TeamMemberDto>;
