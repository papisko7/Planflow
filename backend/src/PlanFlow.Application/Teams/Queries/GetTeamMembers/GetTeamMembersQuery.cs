using MediatR;
using PlanFlow.Application.Teams.Common;

namespace PlanFlow.Application.Teams.Queries.GetTeamMembers;

/// <summary>Lists a team's members (with display names) for any member of that team.</summary>
public record GetTeamMembersQuery(Guid TeamId, Guid RequestingUserId) : IRequest<IReadOnlyList<TeamMemberDto>>;
