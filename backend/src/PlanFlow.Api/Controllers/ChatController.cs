using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanFlow.Api.Common;
using PlanFlow.Application.Chat.Queries.SearchChatMessages;

namespace PlanFlow.Api.Controllers;

[ApiController]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ISender _sender;

    public ChatController(ISender sender)
    {
        _sender = sender;
    }

    // Empty search term matches every message (Contains("")), so this doubles as the initial
    // history load for ChatHub's live stream — the mobile client calls it once on screen mount.
    [HttpGet("api/teams/{teamId:guid}/chat/messages")]
    public async Task<IActionResult> GetMessages(Guid teamId, [FromQuery] string search = "", CancellationToken cancellationToken = default)
    {
        if (User.GetTeamId() != teamId)
        {
            return Forbid();
        }

        var messages = await _sender.Send(new SearchChatMessagesQuery(teamId, search), cancellationToken);
        return Ok(messages);
    }
}
