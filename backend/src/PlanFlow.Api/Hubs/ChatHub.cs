using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PlanFlow.Api.Common;
using PlanFlow.Application.Chat.Commands.SendChatMessage;

namespace PlanFlow.Api.Hubs;

/// <summary>Real-time chat delivery for a team. Persistence goes through the same MediatR command the REST API would use.</summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly ISender _sender;

    public ChatHub(ISender sender)
    {
        _sender = sender;
    }

    public override async Task OnConnectedAsync()
    {
        var teamId = Context.User!.GetTeamId();
        if (teamId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, teamId.Value.ToString());
        }

        await base.OnConnectedAsync();
    }

    public async Task SendMessage(Guid teamId, string content)
    {
        if (Context.User!.GetTeamId() != teamId)
        {
            throw new HubException("You are not a member of this team.");
        }

        var message = await _sender.Send(new SendChatMessageCommand(teamId, Context.User!.GetUserId(), content));

        await Clients.Group(teamId.ToString()).SendAsync("ReceiveMessage", message);
    }

    public async Task TypingIndicator(Guid teamId, bool isTyping)
    {
        if (Context.User!.GetTeamId() != teamId)
        {
            throw new HubException("You are not a member of this team.");
        }

        await Clients.OthersInGroup(teamId.ToString())
            .SendAsync("UserTyping", Context.User!.GetUserId(), isTyping);
    }
}
