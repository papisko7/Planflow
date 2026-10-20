using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Calendar.Commands.DisconnectGoogleCalendar;
using PlanFlow.Application.Calendar.Commands.GoogleOAuthCallback;
using PlanFlow.Application.Calendar.Commands.InitiateGoogleConnect;
using PlanFlow.Application.Calendar.Commands.RequestGoogleSync;
using PlanFlow.Application.Calendar.Queries.GetGoogleCalendarStatus;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Infrastructure.BackgroundJobs;
using Quartz;

namespace PlanFlow.Api.Controllers;

[ApiController]
[Route("api/calendar/google")]
public class CalendarController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ISchedulerFactory _schedulerFactory;

    public CalendarController(ISender sender, ISchedulerFactory schedulerFactory)
    {
        _sender = sender;
        _schedulerFactory = schedulerFactory;
    }

    /// <summary>Lets the mobile Calendar screen show "Connected as ..." or a Connect button.</summary>
    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<GoogleCalendarStatusDto>> Status(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var status = await _sender.Send(new GetGoogleCalendarStatusQuery(userId), cancellationToken);
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        return Ok(status with { Syncing = await GoogleSyncTrigger.IsRunningAsync(scheduler, userId, cancellationToken) });
    }

    /// <summary>
    /// The only way Google is read or changed (no timer): starts the calendar and tasks syncs for the caller's own account, which take in what
    /// changed in Google and send the edits made in the app (the later change wins). The app then watches <c>syncing</c> in the status. A press
    /// within a minute of the last sync starts nothing; a press while a sync is running joins it.
    /// </summary>
    [HttpPost("sync")]
    [Authorize]
    public async Task<ActionResult<GoogleSyncResponse>> SyncNow(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (!await _sender.Send(new RequestGoogleSyncCommand(userId), cancellationToken))
        {
            return Accepted(new GoogleSyncResponse(false));
        }

        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        if (!await GoogleSyncTrigger.IsRunningAsync(scheduler, userId, cancellationToken))
        {
            await GoogleSyncTrigger.StartAsync(scheduler, userId, cancellationToken);
        }

        return Accepted(new GoogleSyncResponse(true));
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
    [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
    public async Task<IActionResult> Callback([FromQuery] GoogleCallbackRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Error))
        {
            return ResultPage("Połączenie anulowane", "Nie udzielono zgody. Możesz wrócić do aplikacji PlanFlow.");
        }

        if (string.IsNullOrEmpty(request.Code) || string.IsNullOrEmpty(request.State))
        {
            throw new UnauthorizedException("OAuth callback is missing the code or state parameter.");
        }

        var result = await _sender.Send(new GoogleOAuthCallbackCommand(request.Code, request.State), cancellationToken);
        var email = System.Net.WebUtility.HtmlEncode(result.ExternalAccountEmail);
        return ResultPage("Połączono z Google Calendar", $"Konto {email} zostało połączone. Wróć do aplikacji PlanFlow.");
    }

    // The callback is opened in the phone's browser, so a small readable page beats raw JSON.
    private ContentResult ResultPage(string title, string message) => Content(
        $"<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{title}</title></head>" +
        $"<body style=\"font-family:sans-serif;text-align:center;padding:48px 24px\"><h2>{title}</h2><p>{message}</p></body></html>",
        "text/html; charset=utf-8");

    [HttpPost("disconnect")]
    [Authorize]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await _sender.Send(new DisconnectGoogleCalendarCommand(User.GetUserId()), cancellationToken);
        return NoContent();
    }
}
