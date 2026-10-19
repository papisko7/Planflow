using MediatR;
using PlanFlow.Application.Chat.Common;

namespace PlanFlow.Application.Chat.Commands.SendChatMessage;

/// <summary>Persists a chat message; real-time delivery goes through SignalR's ChatHub.</summary>
public record SendChatMessageCommand(Guid TeamId, Guid SenderUserId, string Content) : IRequest<ChatMessageDto>;
