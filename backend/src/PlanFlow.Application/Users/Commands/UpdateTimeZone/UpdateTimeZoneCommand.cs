using MediatR;

namespace PlanFlow.Application.Users.Commands.UpdateTimeZone;

/// <summary>
/// Stores the caller's IANA time zone (e.g. "Europe/Warsaw") as reported by the phone. The server needs it to know where a
/// day ends (task deadlines, all-day items) and at what local hour an alert should fire.
/// </summary>
public record UpdateTimeZoneCommand(Guid UserId, string TimeZoneId) : IRequest<Unit>;
