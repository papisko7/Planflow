using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// Immutable audit trail entry for a mutation on a <see cref="TaskItem"/> (thesis Ch.5 traceability).
/// </summary>
public class TaskHistory : BaseEntity
{
    public Guid TaskItemId { get; set; }
    public TaskItem TaskItem { get; set; } = null!;

    public Guid? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }

    public TaskChangeType ChangeType { get; set; }
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
