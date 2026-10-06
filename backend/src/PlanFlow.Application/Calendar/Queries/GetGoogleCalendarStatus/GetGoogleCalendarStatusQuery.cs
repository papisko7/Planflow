using MediatR;

namespace PlanFlow.Application.Calendar.Queries.GetGoogleCalendarStatus;

public record GetGoogleCalendarStatusQuery(Guid UserId) : IRequest<GoogleCalendarStatusDto>;

/// <summary>What the mobile Calendar screen needs to decide between "Connect" and "Connected as ..." UI.</summary>
public record GoogleCalendarStatusDto(bool Connected, string? Email, DateTime? LastSyncedAtUtc);
