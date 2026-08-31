using MediatR;

namespace PlanFlow.Application.Calendar.Commands.InitiateGoogleConnect;

public record InitiateGoogleConnectCommand(Guid UserId) : IRequest<InitiateGoogleConnectResult>;

public record InitiateGoogleConnectResult(string AuthorizationUrl);
