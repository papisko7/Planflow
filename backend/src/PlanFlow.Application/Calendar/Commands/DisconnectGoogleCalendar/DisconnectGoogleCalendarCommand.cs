using MediatR;

namespace PlanFlow.Application.Calendar.Commands.DisconnectGoogleCalendar;

public record DisconnectGoogleCalendarCommand(Guid UserId) : IRequest;
