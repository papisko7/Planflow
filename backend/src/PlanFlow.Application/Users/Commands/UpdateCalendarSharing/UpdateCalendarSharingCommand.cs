using MediatR;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Users.Commands.UpdateCalendarSharing;

/// <summary>How much of the caller's personal (Google) calendar their teammates may see: nothing, availability only, or details.</summary>
public record UpdateCalendarSharingCommand(Guid UserId, CalendarSharingLevel Level) : IRequest<Unit>;
