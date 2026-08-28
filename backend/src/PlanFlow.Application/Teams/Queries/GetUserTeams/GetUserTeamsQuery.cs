using MediatR;
using PlanFlow.Application.Teams.Common;

namespace PlanFlow.Application.Teams.Queries.GetUserTeams;

/// <summary>Lists every team a user belongs to, for the mobile app's team switcher.</summary>
public record GetUserTeamsQuery(Guid UserId) : IRequest<IReadOnlyList<TeamDto>>;
