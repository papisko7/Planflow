using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// One reminder ("alert") of a <see cref="CalendarItem"/>, in one of two forms:
/// an <b>offset</b> ("30 minutes before the start", <see cref="OffsetMinutes"/>), or a <b>day form</b>
/// ("1 day before at 09:00", <see cref="DaysBefore"/> + <see cref="TimeOfDayMinutes"/>) which is the only form that makes sense
/// for an all-day item, because "minutes before midnight" is not something a person thinks in.
/// Exactly one form is filled. <see cref="Anchor"/> says what it counts back from: the start, or (a task only) the deadline.
/// Created by the Google import for events, and by the person in the item form; <c>ItemReminderJob</c> fires them
/// (see <see cref="Services.ReminderSchedule"/> for when).
/// </summary>
public class ItemReminder : BaseEntity
{
    /// <summary>At most this many reminders per item (the same limit Google Calendar's UI is close to).</summary>
    public const int MaxPerItem = 4;

    /// <summary>Longest lead of the offset form (four weeks, in minutes), so the job can look only a little way ahead.</summary>
    public const int MaxOffsetMinutes = 4 * 7 * 24 * 60;

    /// <summary>Most days the day form may count back.</summary>
    public const int MaxDaysBefore = 28;

    public Guid CalendarItemId { get; set; }
    public CalendarItem CalendarItem { get; set; } = null!;

    /// <summary>What the reminder counts back from. An event's reminders always count from its start.</summary>
    public ReminderAnchor Anchor { get; set; } = ReminderAnchor.Start;

    /// <summary>Offset form: minutes before the anchor. Null in the day form.</summary>
    public int? OffsetMinutes { get; set; }

    /// <summary>Day form: whole days before the anchor's day (0 = the same day). Null in the offset form.</summary>
    public int? DaysBefore { get; set; }

    /// <summary>Day form: time of that day as minutes after midnight (09:00 = 540), 0-1439. Null in the offset form.</summary>
    public int? TimeOfDayMinutes { get; set; }

    /// <summary>
    /// The trigger moment this reminder last went off for. A reminder fires once per trigger moment: when the item's time changes the
    /// trigger moves, no longer matches this, and the reminder is due again - without it a reminder would repeat every minute.
    /// </summary>
    public DateTime? LastFiredForUtc { get; set; }

    public static ItemReminder Offset(int minutes, ReminderAnchor anchor = ReminderAnchor.Start) =>
        new() { OffsetMinutes = minutes, Anchor = anchor };

    public static ItemReminder Day(int daysBefore, int timeOfDayMinutes, ReminderAnchor anchor = ReminderAnchor.Start) =>
        new() { DaysBefore = daysBefore, TimeOfDayMinutes = timeOfDayMinutes, Anchor = anchor };

    /// <summary>Same reminder, whatever the row's id: lets a sync or an edit compare a new list with the stored one.</summary>
    public (ReminderAnchor Anchor, int? Offset, int? Days, int? TimeOfDay) Key => (Anchor, OffsetMinutes, DaysBefore, TimeOfDayMinutes);
}
