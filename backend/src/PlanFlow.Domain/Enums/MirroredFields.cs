namespace PlanFlow.Domain.Enums;

/// <summary>
/// The fields of an imported item that exist in Google as well. A bit set stored in <c>CalendarItem.LocalEditedFields</c>: the ones the person
/// changed in the app and a sync has not sent to Google yet. The numbers are stored in the database: only append.
/// </summary>
[Flags]
public enum MirroredFields
{
    None = 0,
    Title = 1,
    Description = 2,

    /// <summary>An event's start, end and all-day flag, changed together.</summary>
    Time = 4,
    Location = 8,
    Color = 16,
    Busy = 32,
    Reminders = 64,

    /// <summary>A Google task's due day.</summary>
    Due = 128,

    /// <summary>A Google task marked done or reopened.</summary>
    Completed = 256
}
