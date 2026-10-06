using MediatR;
using PlanFlow.Application.Teams.Common;

namespace PlanFlow.Application.Teams.Commands.AddTeamMember;

/// <summary>Adds the registered user with the given e-mail to a team; only a member with ManageMembers permission may do this.</summary>
public record AddTeamMemberCommand(
    Guid TeamId,
    Guid RequestingUserId,
    string NewMemberEmail,
    Domain.Enums.TeamRole Role) : IRequest<TeamMemberDto>;
