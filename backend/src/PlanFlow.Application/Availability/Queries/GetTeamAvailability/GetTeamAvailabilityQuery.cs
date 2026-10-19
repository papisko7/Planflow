using MediatR;
using PlanFlow.Application.Availability.Common;

namespace PlanFlow.Application.Availability.Queries.GetTeamAvailability;

/// <summary>Who in the team is free, busy or away over <paramref name="Days"/> days from <paramref name="From"/> (a date), and how loaded they are. Any member may ask.</summary>
public record GetTeamAvailabilityQuery(Guid TeamId, Guid RequestingUserId, DateTime From, int Days) : IRequest<TeamAvailabilityDto>;
