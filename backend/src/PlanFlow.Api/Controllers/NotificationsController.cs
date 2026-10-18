using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanFlow.Api.Common;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Notifications.Inbox;

namespace PlanFlow.Api.Controllers;

/// <summary>The in-app notification inbox and the registration of the phone's push address.</summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly ISender _sender;

    public NotificationsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>The caller's inbox, newest first. Pass <paramref name="before"/> = CreatedAtUtc of the last entry you have to load older ones.</summary>
    [HttpGet]
    public async Task<IActionResult> GetInbox([FromQuery] DateTime? before, [FromQuery] int limit = 30, CancellationToken cancellationToken = default)
    {
        var items = await _sender.Send(new GetNotificationsQuery(User.GetUserId(), before?.ToUniversalTime(), limit), cancellationToken);
        return Ok(items);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        var count = await _sender.Send(new GetUnreadNotificationCountQuery(User.GetUserId()), cancellationToken);
        return Ok(new UnreadCountResponse(count));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new MarkNotificationReadCommand(User.GetUserId(), id), cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await _sender.Send(new MarkAllNotificationsReadCommand(User.GetUserId()), cancellationToken);
        return NoContent();
    }

    /// <summary>Marks the ticked entries as read.</summary>
    [HttpPost("read")]
    public async Task<IActionResult> MarkSelectedRead(NotificationIdsRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new MarkSelectedNotificationsReadCommand(User.GetUserId(), request.Ids ?? new List<Guid>()), cancellationToken);
        return NoContent();
    }

    /// <summary>Deletes the ticked entries.</summary>
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteSelected(NotificationIdsRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteSelectedNotificationsCommand(User.GetUserId(), request.Ids ?? new List<Guid>()), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteNotificationCommand(User.GetUserId(), id), cancellationToken);
        return NoContent();
    }

    /// <summary>"Clear all" - keeps only entries whose push has not been delivered yet.</summary>
    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        await _sender.Send(new ClearNotificationsCommand(User.GetUserId()), cancellationToken);
        return NoContent();
    }

    /// <summary>Tells the server this phone can receive pushes. Safe to call on every sign-in.</summary>
    [HttpPut("push-token")]
    public async Task<IActionResult> RegisterPushToken(RegisterPushTokenRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new RegisterPushTokenCommand(User.GetUserId(), request.Token, request.Platform ?? string.Empty), cancellationToken);
        return NoContent();
    }

    /// <summary>Forgets this phone's push address (called on sign-out).</summary>
    [HttpDelete("push-token")]
    public async Task<IActionResult> UnregisterPushToken([FromQuery] string token, CancellationToken cancellationToken)
    {
        await _sender.Send(new UnregisterPushTokenCommand(User.GetUserId(), token), cancellationToken);
        return NoContent();
    }
}
