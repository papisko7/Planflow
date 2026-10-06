using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Auth.Commands.ChangePassword;
using PlanFlow.Application.Auth.Commands.ResendEmailVerification;
using PlanFlow.Application.Auth.Commands.VerifyEmail;
using PlanFlow.Application.Users.Commands.UpdateNotificationPreferences;
using PlanFlow.Application.Users.Commands.UpdateProfile;
using PlanFlow.Application.Users.Common;
using PlanFlow.Application.Users.Queries.GetNotificationPreferences;
using PlanFlow.Application.Users.Queries.GetProfile;

namespace PlanFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users/me")]
public class UsersController : ControllerBase
{
    private readonly ISender _sender;

    public UsersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var profile = await _sender.Send(new GetProfileQuery(User.GetUserId()), cancellationToken);
        return Ok(profile);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await _sender.Send(
            new UpdateProfileCommand(User.GetUserId(), request.DisplayName, request.Email, request.CurrentPassword),
            cancellationToken);

        return Ok(profile);
    }

    [HttpGet("notification-preferences")]
    public async Task<IActionResult> GetNotificationPreferences(CancellationToken cancellationToken)
    {
        var preferences = await _sender.Send(new GetNotificationPreferencesQuery(User.GetUserId()), cancellationToken);
        return Ok(preferences);
    }

    [HttpPut("notification-preferences")]
    public async Task<IActionResult> UpdateNotificationPreferences(NotificationPreferencesDto request, CancellationToken cancellationToken)
    {
        var preferences = await _sender.Send(new UpdateNotificationPreferencesCommand(User.GetUserId(), request), cancellationToken);
        return Ok(preferences);
    }

    // Password and e-mail confirmation live here (not in AuthController) because they need a signed-in caller;
    // they share the strict "auth" rate limit since a code or password can be guessed.
    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ChangePasswordCommand(User.GetUserId(), request.CurrentPassword, request.NewPassword, request.RefreshToken),
            cancellationToken);
        return NoContent();
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new VerifyEmailCommand(User.GetUserId(), request.Code), cancellationToken);
        return NoContent();
    }

    [HttpPost("resend-verification")]
    [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
    public async Task<IActionResult> ResendVerification(CancellationToken cancellationToken)
    {
        await _sender.Send(new ResendEmailVerificationCommand(User.GetUserId()), cancellationToken);
        return NoContent();
    }
}
