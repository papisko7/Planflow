using MediatR;
using PlanFlow.Application.Auth.Common;

namespace PlanFlow.Application.Auth.Commands.Register;

/// <summary>Creates a new user account and immediately issues a token pair (no team yet — see <see cref="Application.Auth.Commands.Login.LoginCommand"/>).</summary>
public record RegisterCommand(string Email, string Password, string DisplayName) : IRequest<AuthResultDto>;
