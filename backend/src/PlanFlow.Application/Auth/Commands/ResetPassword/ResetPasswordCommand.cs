using MediatR;

namespace PlanFlow.Application.Auth.Commands.ResetPassword;

/// <summary>Sets a new password using the code from the reset e-mail, and signs the account out on every device.</summary>
public record ResetPasswordCommand(string Email, string Code, string NewPassword) : IRequest;
