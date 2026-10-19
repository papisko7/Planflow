using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Services;

/// <summary>
/// The arithmetic of repeating: given a rule, which moments are its occurrences. Pure (no database, no clock), so every rule can be
/// unit-tested. Occurrences are worked out on the calendar in the series' own time zone and only then turned into UTC, which is why a
/// 09:00 meeting stays at 09:00 on the wall clock when daylight saving starts or ends.
/// </summary>
public static class RecurrenceRules
{
    /// <summary>Largest "every N" a rule may have (every 99 days / weeks / months / years).</summary>
    public const int MaxInterval = 99;

    /// <summary>Safety net against a rule that would be walked for ever (a daily rule asked for the year 9000).</summary>
    private const int MaxStepsPerCall = 40_000;

    /// <summary>Bit mask of the days of a week: Monday = 1 ... Sunday = 64.</summary>
    public const int AllWeekdays = 0b1111111;

    public static int Weekdays(DayOfWeek day) => 1 << (((int)day + 6) % 7);

    public static bool IsValidWeekdayMask(int mask) => mask is >= 0 and <= AllWeekdays;

    /// <summary>The days a weekly rule really uses: its mask, or the weekday of the first occurrence when the mask is 0. For comparing two rules.</summary>
    public static int EffectiveWeekdays(RecurrenceSeries series, bool isAllDay)
    {
        if (series.Weekdays != 0)
        {
            return series.Weekdays;
        }

        var zone = isAllDay ? TimeZoneInfo.Utc : AllDayTime.ZoneOrUtc(series.TimeZoneId);
        return Weekdays(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(series.AnchorUtc, DateTimeKind.Utc), zone).DayOfWeek);
    }

    /// <summary>
    /// The occurrences strictly after <paramref name="afterUtc"/> and up to <paramref name="throughUtc"/> (inclusive), oldest first, never
    /// past the series' end. <paramref name="isAllDay"/> items are read in UTC: their day is stored as midnight UTC and means that date everywhere.
    /// </summary>
    public static IEnumerable<DateTime> Occurrences(RecurrenceSeries series, bool isAllDay, DateTime afterUtc, DateTime throughUtc)
    {
        var zone = isAllDay ? TimeZoneInfo.Utc : AllDayTime.ZoneOrUtc(series.TimeZoneId);
        var anchor = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(series.AnchorUtc, DateTimeKind.Utc), zone);
        var interval = Math.Max(1, series.Interval);

        var steps = 0;
        foreach (var day in LocalDays(series.Frequency, interval, series.Weekdays, anchor.Date))
        {
            var due = WallClockToUtc(day + anchor.TimeOfDay, zone);
            if (due > throughUtc || (series.EndsAtUtc is { } end && due > end) || ++steps > MaxStepsPerCall)
            {
                yield break;
            }

            if (due > afterUtc)
            {
                yield return due;
            }
        }
    }

    /// <summary>The first occurrence strictly after <paramref name="afterUtc"/>, or null when the series has ended.</summary>
    public static DateTime? NextAfter(RecurrenceSeries series, bool isAllDay, DateTime afterUtc) =>
        Occurrences(series, isAllDay, afterUtc, DateTime.MaxValue.AddDays(-2)).Cast<DateTime?>().FirstOrDefault();

    /// <summary>The calendar days of the rule, ascending, starting at (or after) the anchor day.</summary>
    private static IEnumerable<DateTime> LocalDays(RecurrenceFrequency frequency, int interval, int weekdays, DateTime anchorDay)
    {
        switch (frequency)
        {
            case RecurrenceFrequency.Daily:
                for (var i = 0; ; i++)
                {
                    yield return anchorDay.AddDays((long)i * interval);
                }

            case RecurrenceFrequency.Weekly:
                var mask = IsValidWeekdayMask(weekdays) && weekdays != 0 ? weekdays : Weekdays(anchorDay.DayOfWeek);
                var firstMonday = anchorDay.AddDays(-(((int)anchorDay.DayOfWeek + 6) % 7));
                for (var week = 0; ; week++)
                {
                    var monday = firstMonday.AddDays(7L * interval * week);
                    for (var d = 0; d < 7; d++)
                    {
                        var day = monday.AddDays(d);
                        if ((mask & (1 << d)) != 0 && day >= anchorDay)
                        {
                            yield return day;
                        }
                    }
                }

            case RecurrenceFrequency.Monthly:
                for (var i = 0; ; i++)
                {
                    var first = new DateTime(anchorDay.Year, anchorDay.Month, 1).AddMonths(i * interval);
                    // The 31st in a 30-day month falls on the last day of that month.
                    yield return first.AddDays(Math.Min(anchorDay.Day, DateTime.DaysInMonth(first.Year, first.Month)) - 1);
                }

            default: // Yearly
                for (var i = 0; ; i++)
                {
                    var year = anchorDay.Year + i * interval;
                    // 29 Feb comes back as 28 Feb in a year that has no 29th.
                    yield return new DateTime(year, anchorDay.Month, Math.Min(anchorDay.Day, DateTime.DaysInMonth(year, anchorDay.Month)));
                }
        }
    }

    /// <summary>A wall-clock time in <paramref name="zone"/> as UTC. A time the clock skips (spring forward) moves an hour on; a time it shows twice (autumn) means the first.</summary>
    public static DateTime WallClockToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }
}
