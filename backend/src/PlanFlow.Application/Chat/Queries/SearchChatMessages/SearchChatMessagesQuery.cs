using MediatR;
using PlanFlow.Application.Chat.Common;

namespace PlanFlow.Application.Chat.Queries.SearchChatMessages;

/// <summary>[EXT] Full-text-ish search (substring match) over a team's chat history.</summary>
public record SearchChatMessagesQuery(Guid TeamId, string SearchTerm) : IRequest<IReadOnlyList<ChatMessageDto>>;
