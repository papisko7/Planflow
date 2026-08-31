using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Calendar.Commands.DisconnectGoogleCalendar;
using PlanFlow.Application.Calendar.Commands.GoogleOAuthCallback;
using PlanFlow.Application.Calendar.Commands.InitiateGoogleConnect;
using PlanFlow.Application.Common.Exceptions;

namespace PlanFlow.Api.Controllers;

[ApiController]
[Route("api/calendar/google")]
public class CalendarController : ControllerBase
{
    private readonly ISender _sender;

    public CalendarController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Authenticated leg: mints PKCE + state and returns the Google consent URL for the mobile client to open.</summary>
    [HttpGet("connect")]
    [Authorize]
    public async Task<ActionResult<GoogleConnectResponse>> Connect(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new InitiateGoogleConnectCommand(User.GetUserId()), cancellationToken);
        return Ok(new GoogleConnectResponse(result.AuthorizationUrl));
    }

    /// <summary>
    /// Google's redirect target. Unauthenticated by design — the browser/webview following this
    /// redirect carries no PlanFlow JWT — trust comes from the state token minted in <see cref="Connect"/>.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] GoogleCallbackRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Error))
        {
            // User denied consent on Google's screen — not a server error, just an incomplete flow.
            return Ok(new { connected = false, reason = request.Error });
        }

        if (string.IsNullOrEmpty(request.Code) || string.IsNullOrEmpty(request.State))
        {
            throw new UnauthorizedException("OAuth callback is missing the code or state parameter.");
        }

        var result = await _sender.Send(new GoogleOAuthCallbackCommand(request.Code, request.State), cancellationToken);
        return Ok(result);
    }

    [HttpPost("disconnect")]
    [Authorize]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await _sender.Send(new DisconnectGoogleCalendarCommand(User.GetUserId()), cancellationToken);
        return NoContent();
    }
}
