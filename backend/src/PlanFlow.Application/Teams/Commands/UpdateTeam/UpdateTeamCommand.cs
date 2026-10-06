using MediatR;
using PlanFlow.Application.Teams.Common;

namespace PlanFlow.Application.Teams.Commands.UpdateTeam;

/// <summary>Renames a team / changes its description. Owner and Admin only; a personal workspace cannot be edited.</summary>
public record UpdateTeamCommand(Guid TeamId, Guid RequestingUserId, string Name, string? Description) : IRequest<TeamDto>;
