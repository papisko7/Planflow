using MediatR;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Queries.GetTasksByTeam;

/// <summary>Fetches a team's tasks sorted by Urgency Score, descending — the dashboard's primary read.</summary>
public record GetTasksByTeamQuery(Guid TeamId) : IRequest<IReadOnlyList<TaskDto>>;
