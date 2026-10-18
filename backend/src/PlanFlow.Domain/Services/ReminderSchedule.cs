using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Services;

/// <summary>
/// When a reminder goes off. Pure functions (no clock, no database), so the rules - all-day items, time zones, "end of the deadline day" -
/// are unit-tested and the Quartz job only loads, asks and saves.
/// </summary>
public static class ReminderSchedule
{
    /// <summary>
    /// A reminder is still sent when its moment passed up to this long ago (the job runs every minute, but may have been down for a moment).
    /// An older one is never "caught up": reminders of a freshly imported or just-edited item whose moment is long gone stay silent.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The moment the reminder should go off, or null when it never can: the item lacks the moment it counts back from (a task
    /// without a planned start, an event with a deadline anchor), or a day-form time falls after the moment itself ("the same day at 23:00" for a 10:00 event).
    /// <paramref name="zone"/> is the clock of the person the reminder is for: a day-form "09:00" means 09:00 on their wall.
    /// </summary>
    public static DateTime? TriggerUtc(CalendarItem item, ItemReminder reminder, TimeZoneInfo zone)
    {
        var anchorStored = reminder.Anchor == ReminderAnchor.Deadline ? item.DeadlineUtc : item.StartUtc;
        if (anchorStored is null || (reminder.Anchor == ReminderAnchor.Deadline && item.TaskType != TaskType.Task))
        {
            return null;
        }

        var anchorMoment = AnchorMoment(item, reminder.Anchor, anchorStored.Value, zone);

        if (reminder.OffsetMinutes is { } offset)
        {
            return anchorMoment.AddMinutes(-offset);
        }

        if (reminder.DaysBefore is not { } daysBefore || reminder.TimeOfDayMinutes is not { } timeOfDay)
        {
            return null; // neither form is filled (the database forbids it; a half-built object in memory must not throw here)
        }

        // The day the anchor falls on: an all-day item's date is read literally, a timed one's is the date on the person's own clock.
        var day = item.IsAllDay
            ? anchorStored.Value.Date
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(anchorStored.Value, DateTimeKind.Utc), zone).Date;

        var local = DateTime.SpecifyKind(day.AddDays(-daysBefore).AddMinutes(timeOfDay), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1); // the clock skipped this time on a daylight-saving day: the first moment that exists
        }

        var trigger = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, zone), DateTimeKind.Utc);

        // A reminder is for something still ahead. For a timed item that is the moment itself; an all-day item is "ahead" for its whole day
        // (Google writes "the day itself at 09:00" for an all-day event, which is after its midnight start).
        var limit = item.IsAllDay ? AllDayTime.StartOfLocalDayUtc(anchorStored.Value.AddDays(1), zone) : anchorMoment;
        return trigger > limit ? null : trigger;
    }

    /// <summary>True when the reminder should be sent now: its moment has come, is not older than <see cref="Grace"/>, and it did not already go off for that very moment.</summary>
    public static bool IsDue(DateTime? triggerUtc, DateTime? lastFiredForUtc, DateTime nowUtc)
    {
        if (triggerUtc is not { } trigger || trigger > nowUtc || nowUtc - trigger > Grace)
        {
            return false;
        }

        return lastFiredForUtc is not { } last || Math.Abs((last - trigger).TotalSeconds) >= 1;
    }

    /// <summary>
    /// The real moment an anchor stands for. A timed item: the stored moment. An all-day item: a Start is the beginning of that day on the
    /// person's clock, a Deadline is the END of it (a task due on the 20th is not late until the 20th is over).
    /// </summary>
    private static DateTime AnchorMoment(CalendarItem item, ReminderAnchor anchor, DateTime stored, TimeZoneInfo zone)
    {
        if (!item.IsAllDay)
        {
            return DateTime.SpecifyKind(stored, DateTimeKind.Utc);
        }

        return AllDayTime.StartOfLocalDayUtc(anchor == ReminderAnchor.Deadline ? stored.AddDays(1) : stored, zone);
    }
}
