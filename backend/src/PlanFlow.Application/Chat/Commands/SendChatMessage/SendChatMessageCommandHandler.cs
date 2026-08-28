using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Chat.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Chat.Commands.SendChatMessage;

public class SendChatMessageCommandHandler : IRequestHandler<SendChatMessageCommand, ChatMessageDto>
{
    private readonly IApplicationDbContext _context;

    public SendChatMessageCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ChatMessageDto> Handle(SendChatMessageCommand request, CancellationToken cancellationToken)
    {
        var message = new ChatMessage
        {
            TeamId = request.TeamId,
            SenderUserId = request.SenderUserId,
            Content = request.Content
        };

        _context.ChatMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);

        var sender = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.SenderUserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.SenderUserId);

        return new ChatMessageDto(message.Id, message.TeamId, message.SenderUserId, sender.DisplayName, message.Content, message.CreatedAtUtc);
    }
}
