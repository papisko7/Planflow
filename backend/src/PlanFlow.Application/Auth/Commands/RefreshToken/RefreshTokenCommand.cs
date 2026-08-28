using MediatR;
using PlanFlow.Application.Auth.Common;

namespace PlanFlow.Application.Auth.Commands.RefreshToken;

/// <summary>
/// Rotates a refresh token for a new access/refresh pair. The new access token never carries a
/// team context (that's re-established via <see cref="Application.Auth.Commands.Login.LoginCommand"/>) —
/// keeps rotation independent of the not-yet-built Phase 4.3 team-switch flow.
/// </summary>
public record RefreshTokenCommand(Guid UserId, string RefreshToken) : IRequest<AuthResultDto>;
