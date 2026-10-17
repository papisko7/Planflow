using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Alerts.Common;

public record AlertDto(
    Guid Id,
    Guid UserId,
    Guid? CalendarItemId,
    Domain.Enums.AlertType Type,
    string Message,
    bool IsRead,
    DateTime CreatedAtUtc)
{
    public static AlertDto FromEntity(Alert alert) => new(
        alert.Id, alert.UserId, alert.CalendarItemId, alert.Type, alert.Message, alert.IsRead, alert.CreatedAtUtc);
}
