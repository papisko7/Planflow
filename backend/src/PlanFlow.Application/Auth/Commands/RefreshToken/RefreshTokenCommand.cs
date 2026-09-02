using MediatR;
using PlanFlow.Application.Auth.Common;

namespace PlanFlow.Application.Auth.Commands.RefreshToken;

/// <summary>
/// Rotates a refresh token for a new access/refresh pair. An optional TeamId re-establishes team
/// context on the new access token (mirrors <see cref="Application.Auth.Commands.Login.LoginCommand"/>'s
/// membership check) without forcing the client to re-collect the user's password just to switch
/// the active team.
/// </summary>
public record RefreshTokenCommand(Guid UserId, string RefreshToken, Guid? TeamId = null) : IRequest<AuthResultDto>;
