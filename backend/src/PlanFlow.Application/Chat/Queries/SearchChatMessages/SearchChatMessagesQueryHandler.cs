using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Chat.Common;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Chat.Queries.SearchChatMessages;

public class SearchChatMessagesQueryHandler : IRequestHandler<SearchChatMessagesQuery, IReadOnlyList<ChatMessageDto>>
{
    private readonly IApplicationDbContext _context;

    public SearchChatMessagesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ChatMessageDto>> Handle(SearchChatMessagesQuery request, CancellationToken cancellationToken)
    {
        // Case-insensitive substring match via ToLower(), kept provider-agnostic (no Npgsql-specific
        // ILike) since this project reference (Application) must not depend on a specific EF provider.
        var searchTermLower = request.SearchTerm.ToLower();

        var messages = await _context.ChatMessages
            .AsNoTracking()
            .Include(m => m.SenderUser)
            .Where(m => m.TeamId == request.TeamId && m.Content.ToLower().Contains(searchTermLower))
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return messages.Select(ChatMessageDto.FromEntity).ToList();
    }
}
