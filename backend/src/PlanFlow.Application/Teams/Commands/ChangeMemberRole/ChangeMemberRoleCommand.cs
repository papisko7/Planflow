using MediatR;
using PlanFlow.Application.Teams.Common;

namespace PlanFlow.Application.Teams.Commands.ChangeMemberRole;

/// <summary>Changes a member's role (Member &lt;-&gt; Admin). Needs ManageMembers; only the owner may grant or take away Admin; the owner's role never changes.</summary>
public record ChangeMemberRoleCommand(
    Guid TeamId,
    Guid RequestingUserId,
    Guid MemberUserId,
    Domain.Enums.TeamRole NewRole) : IRequest<TeamMemberDto>;
