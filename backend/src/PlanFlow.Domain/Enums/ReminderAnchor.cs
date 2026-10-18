namespace PlanFlow.Domain.Enums;

/// <summary>The moment of an item a reminder counts back from. The numbers are stored in the database and sent to the app: only append.</summary>
public enum ReminderAnchor
{
    /// <summary>An event's start, or a task's planned start.</summary>
    Start = 0,

    /// <summary>A task's deadline (an event has none).</summary>
    Deadline = 1
}
