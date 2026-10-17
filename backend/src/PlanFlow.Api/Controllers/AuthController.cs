using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Auth.Commands.Login;
using PlanFlow.Application.Auth.Commands.RefreshToken;
using PlanFlow.Application.Auth.Commands.Register;
using PlanFlow.Application.Auth.Commands.RequestPasswordReset;
using PlanFlow.Application.Auth.Commands.ResetPassword;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Auth.Queries.IdentifyUser;

namespace PlanFlow.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
// The strict auth limit is applied per action (login/register), NOT to the whole controller:
// refresh needs a valid single-use token so it is not brute-forceable, and the mobile app calls it
// every time it switches team scope — sharing the 5/min login limit made normal use return 429.
public class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
    {
        _sender = sender;
    }

    // Returns the UserId for a given email so the mobile app can check per-user PIN state
    // before presenting the credential step. Rate-limited like login to prevent enumeration at scale.
    [HttpPost("identify")]
    [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
    public async Task<ActionResult<IdentifyResponse>> Identify(IdentifyRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new IdentifyUserQuery(request.Email), cancellationToken);
        return Ok(new IdentifyResponse(result.UserId));
    }

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new RegisterCommand(request.Email, request.Password, request.DisplayName, request.DeviceId, request.DeviceName),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
    public async Task<ActionResult<AuthResultDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new LoginCommand(request.Email, request.Password, request.TeamId, request.DeviceId, request.DeviceName),
            cancellationToken);
        return Ok(result);
    }

    // Always 204, whether or not the e-mail has an account (no account enumeration).
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new RequestPasswordResetCommand(request.Email), cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new ResetPasswordCommand(request.Email, request.Code, request.NewPassword), cancellationToken);
        return NoContent();
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResultDto>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new RefreshTokenCommand(request.UserId, request.RefreshToken, request.TeamId), cancellationToken);
        return Ok(result);
    }
}
