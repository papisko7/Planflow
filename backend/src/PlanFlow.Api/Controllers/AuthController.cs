using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Auth.Commands.Login;
using PlanFlow.Application.Auth.Commands.RefreshToken;
using PlanFlow.Application.Auth.Commands.Register;
using PlanFlow.Application.Auth.Common;

namespace PlanFlow.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
public class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new RegisterCommand(request.Email, request.Password, request.DisplayName), cancellationToken);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResultDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new LoginCommand(request.Email, request.Password, request.TeamId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResultDto>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new RefreshTokenCommand(request.UserId, request.RefreshToken), cancellationToken);
        return Ok(result);
    }
}
