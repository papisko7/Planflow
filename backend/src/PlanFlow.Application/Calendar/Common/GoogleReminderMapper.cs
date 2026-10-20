using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Calendar.Common;

/// <summary>
/// Turns Google's reminders ("N minutes before the start") into <see cref="ItemReminder"/>s.
/// A timed event keeps the offset form. For an all-day event Google counts the same minutes back from MIDNIGHT of its first
/// day, so 900 means "15 hours before midnight" = the previous day at 09:00 - nobody thinks that way, so it is converted to
/// "1 day before at 09:00" (the day form). Pure functions, no I/O.
/// </summary>
public static class GoogleReminderMapper
{
    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Distinct reminders in Google's order, at most <see cref="ItemReminder.MaxPerItem"/> (the first ones win).
    /// Both Google methods (pop-up and e-mail) arrive as plain minutes, so two methods at the same time collapse into one reminder.
    /// A reminder that cannot be expressed (an all-day "minutes" value Google would never send) is skipped, not an error.
    /// </summary>
    public static IReadOnlyList<ItemReminder> Map(IEnumerable<int> minutesBeforeStart, bool isAllDay)
    {
        var result = new List<ItemReminder>();
        var seen = new HashSet<(Domain.Enums.ReminderAnchor, int?, int?, int?)>();

        foreach (var minutes in minutesBeforeStart)
        {
            var reminder = isAllDay ? ToDayForm(minutes) : ToOffsetForm(minutes);
            if (reminder is null || !seen.Add(reminder.Key))
            {
                continue;
            }

            result.Add(reminder);
            if (result.Count == ItemReminder.MaxPerItem)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// The reverse of <see cref="Map"/>: the reminders as "minutes before the start", the form Google takes. An offset reminder is its minutes;
    /// a day reminder counts back from midnight of the first day, as Google does for an all-day event (1 day before at 09:00 = 900).
    /// A reminder that counts back from a deadline has no counterpart in a Google event and is left out.
    /// </summary>
    public static IReadOnlyList<int> ToMinutes(IEnumerable<(Domain.Enums.ReminderAnchor Anchor, int? Offset, int? Days, int? TimeOfDay)> reminders) =>
        reminders
            .Where(r => r.Anchor == Domain.Enums.ReminderAnchor.Start)
            .Select(r => r.Offset ?? (r.Days ?? 0) * MinutesPerDay - (r.TimeOfDay ?? 0))
            .Distinct()
            .OrderBy(minutes => minutes)
            .ToList();

    private static ItemReminder? ToOffsetForm(int minutes) => minutes >= 0 ? ItemReminder.Offset(minutes) : null;

    /// <summary>
    /// Whole days back plus a time of day: 900 min -> 1 day before at 09:00, 1440 -> 1 day before at 00:00, 0 -> the day itself at 00:00.
    /// A negative value is how a reminder later than midnight of the day itself would be written: -540 -> the day itself at 09:00.
    /// </summary>
    private static ItemReminder? ToDayForm(int minutes)
    {
        var days = minutes > 0 ? (minutes + MinutesPerDay - 1) / MinutesPerDay : 0;
        var timeOfDay = days * MinutesPerDay - minutes;
        return timeOfDay is >= 0 and < MinutesPerDay ? ItemReminder.Day(days, timeOfDay) : null;
    }
}
