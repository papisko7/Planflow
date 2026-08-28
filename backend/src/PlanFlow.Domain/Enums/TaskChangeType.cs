namespace PlanFlow.Domain.Enums;

/// <summary>
/// Kind of mutation recorded in a <see cref="Entities.TaskHistory"/> entry.
/// </summary>
public enum TaskChangeType
{
    Created = 0,
    StatusChanged = 1,
    Reassigned = 2,
    UrgencyRecalculated = 3,
    FieldUpdated = 4,
    Deleted = 5
}
