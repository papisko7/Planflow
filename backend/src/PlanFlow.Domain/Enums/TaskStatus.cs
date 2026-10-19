namespace PlanFlow.Domain.Enums;

/// <summary>
/// One shared status list for both item types; <see cref="Services.TaskStatusRules"/> says which values are legal for a Task
/// and which for an Event. The numbers are stored in the database, so existing values keep their meaning
/// (0 was "Todo", 3 was "Done"); 2 was "Blocked" and is retired — "blocked" is now derived from BlockedByTaskId.
/// </summary>
public enum TaskStatus
{
    Backlog = 0,
    InProgress = 1,
    Completed = 3,

    Cancelled = 4,

    // Event statuses (same words as Google Calendar's event status)
    Confirmed = 5,
    Tentative = 6
}
