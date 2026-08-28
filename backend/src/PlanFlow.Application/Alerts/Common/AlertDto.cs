using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Alerts.Common;

public record AlertDto(
    Guid Id,
    Guid UserId,
    Guid? TaskItemId,
    Domain.Enums.AlertType Type,
    string Message,
    bool IsRead,
    DateTime CreatedAtUtc)
{
    public static AlertDto FromEntity(Alert alert) => new(
        alert.Id, alert.UserId, alert.TaskItemId, alert.Type, alert.Message, alert.IsRead, alert.CreatedAtUtc);
}
