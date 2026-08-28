using MediatR;
using PlanFlow.Application.Teams.Common;

namespace PlanFlow.Application.Teams.Commands.CreateTeam;

/// <summary>Creates a team and enrolls its creator as <see cref="Domain.Enums.TeamRole.Owner"/>.</summary>
public record CreateTeamCommand(string Name, string? Description, Guid OwnerUserId) : IRequest<TeamDto>;
