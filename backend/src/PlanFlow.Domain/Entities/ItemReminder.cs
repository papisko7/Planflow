using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// One reminder ("alert") of a <see cref="CalendarItem"/>, in one of two forms:
/// an <b>offset</b> ("30 minutes before the start", <see cref="OffsetMinutes"/>), or a <b>day form</b>
/// ("1 day before at 09:00", <see cref="DaysBefore"/> + <see cref="TimeOfDayMinutes"/>) which is the only form that makes sense
/// for an all-day item, because "minutes before midnight" is not something a person thinks in.
/// Exactly one form is filled. Created here by the Google import; the job that fires reminders and the form that edits them come later.
/// </summary>
public class ItemReminder : BaseEntity
{
    /// <summary>At most this many reminders per item (the same limit Google Calendar's UI is close to).</summary>
    public const int MaxPerItem = 4;

    public Guid CalendarItemId { get; set; }
    public CalendarItem CalendarItem { get; set; } = null!;

    /// <summary>Offset form: minutes before the item's start. Null in the day form.</summary>
    public int? OffsetMinutes { get; set; }

    /// <summary>Day form: whole days before the item's day (0 = the same day). Null in the offset form.</summary>
    public int? DaysBefore { get; set; }

    /// <summary>Day form: time of that day as minutes after midnight (09:00 = 540), 0-1439. Null in the offset form.</summary>
    public int? TimeOfDayMinutes { get; set; }

    public static ItemReminder Offset(int minutes) => new() { OffsetMinutes = minutes };

    public static ItemReminder Day(int daysBefore, int timeOfDayMinutes) =>
        new() { DaysBefore = daysBefore, TimeOfDayMinutes = timeOfDayMinutes };

    /// <summary>Same reminder, whatever the row's id: lets a sync compare Google's list with the stored one.</summary>
    public (int? Offset, int? Days, int? TimeOfDay) Key => (OffsetMinutes, DaysBefore, TimeOfDayMinutes);
}
