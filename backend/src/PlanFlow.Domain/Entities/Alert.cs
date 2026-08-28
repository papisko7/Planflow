using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// A notification generated for a user, typically by the AlertingJob (Phase 1.5).
/// </summary>
public class Alert : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid? TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public AlertType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
