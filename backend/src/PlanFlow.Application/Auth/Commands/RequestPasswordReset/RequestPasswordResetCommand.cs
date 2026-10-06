using MediatR;

namespace PlanFlow.Application.Auth.Commands.RequestPasswordReset;

/// <summary>Sends a 6-digit reset code to the e-mail address. Always succeeds, so the endpoint cannot be used to find out which e-mails have an account.</summary>
public record RequestPasswordResetCommand(string Email) : IRequest;
