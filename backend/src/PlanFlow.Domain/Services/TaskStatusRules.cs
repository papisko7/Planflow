using PlanFlow.Domain.Enums;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Domain.Services;

/// <summary>
/// Which statuses are legal for which item type. A Task moves through Backlog -> InProgress -> Completed;
/// an Event is just Confirmed, Tentative or Cancelled (it happens, it does not get "done"). Pure functions, no database.
/// </summary>
public static class TaskStatusRules
{
    public static TaskStatus DefaultFor(TaskType type) => type == TaskType.Event ? TaskStatus.Confirmed : TaskStatus.Backlog;

    public static bool IsValidFor(TaskType type, TaskStatus status) => type == TaskType.Event
        ? status is TaskStatus.Confirmed or TaskStatus.Tentative or TaskStatus.Cancelled
        : status is TaskStatus.Backlog or TaskStatus.InProgress or TaskStatus.Completed or TaskStatus.Cancelled;

    /// <summary>Finished items leave the ranking: Completed and Cancelled.</summary>
    public static bool IsFinished(TaskStatus status) => status is TaskStatus.Completed or TaskStatus.Cancelled;

    /// <summary>
    /// Keeps a status valid when an item changes type: a status that is legal for the new type stays,
    /// otherwise the item falls back to the new type's default.
    /// </summary>
    public static TaskStatus Normalize(TaskType type, TaskStatus status) =>
        IsValidFor(type, status) ? status : DefaultFor(type);
}
