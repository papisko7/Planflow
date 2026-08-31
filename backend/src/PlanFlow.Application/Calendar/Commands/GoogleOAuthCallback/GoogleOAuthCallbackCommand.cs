using MediatR;
using PlanFlow.Application.Calendar.Common;

namespace PlanFlow.Application.Calendar.Commands.GoogleOAuthCallback;

public record GoogleOAuthCallbackCommand(string Code, string State) : IRequest<CalendarIntegrationDto>;
