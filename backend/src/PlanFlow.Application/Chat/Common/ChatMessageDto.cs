using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Chat.Common;

public record ChatMessageDto(
    Guid Id,
    Guid TeamId,
    Guid SenderUserId,
    string SenderDisplayName,
    string Content,
    DateTime CreatedAtUtc)
{
    public static ChatMessageDto FromEntity(ChatMessage message) => new(
        message.Id,
        message.TeamId,
        message.SenderUserId,
        message.SenderUser.DisplayName,
        message.Content,
        message.CreatedAtUtc);
}
