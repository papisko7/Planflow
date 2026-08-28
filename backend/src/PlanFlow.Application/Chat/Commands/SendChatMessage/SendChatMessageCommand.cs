using MediatR;
using PlanFlow.Application.Chat.Common;

namespace PlanFlow.Application.Chat.Commands.SendChatMessage;

/// <summary>[EXT] Persists a chat message; real-time delivery goes through SignalR's ChatHub (Phase 5.1).</summary>
public record SendChatMessageCommand(Guid TeamId, Guid SenderUserId, string Content) : IRequest<ChatMessageDto>;
