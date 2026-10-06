using MediatR;

namespace PlanFlow.Application.Teams.Commands.RemoveTeamMember;

/// <summary>
/// Removes a member from a team. A member may always remove themselves (leave); removing someone
/// else requires ManageMembers. The team owner can never be removed.
/// </summary>
public record RemoveTeamMemberCommand(Guid TeamId, Guid RequestingUserId, Guid MemberUserId) : IRequest;
