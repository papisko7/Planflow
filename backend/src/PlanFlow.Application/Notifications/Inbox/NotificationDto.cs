using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Notifications.Inbox;

/// <summary>One inbox entry as the app shows it. <paramref name="TaskId"/> / <paramref name="TeamId"/> tell the app where a tap should lead.</summary>
public record NotificationDto(
    Guid Id,
    NotificationEvent Event,
    string Title,
    string Body,
    DateTime CreatedAtUtc,
    bool IsRead,
    Guid? TeamId,
    Guid? TaskId)
{
    public static NotificationDto FromEntity(Notification n) =>
        new(n.Id, n.Event, n.Title, n.Body, n.CreatedAtUtc, n.ReadAtUtc != null, n.TeamId, n.TaskId);
}
