using MediatR;

namespace PlanFlow.Application.Teams.Commands.DeleteTeam;

/// <summary>Deletes a team together with its tasks, chat and memberships. Owner only; a personal workspace cannot be deleted.</summary>
public record DeleteTeamCommand(Guid TeamId, Guid RequestingUserId) : IRequest;
