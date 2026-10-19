using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Services;

public enum LoadLevel
{
    /// <summary>Below 60 % of the available time.</summary>
    Light = 0,
    /// <summary>60 % up to and including 100 %.</summary>
    Balanced = 1,
    /// <summary>More work than available time.</summary>
    Overloaded = 2
}

/// <summary>An open task of a person: when it may start, when it is due (both real moments) and this person's share of its effort in hours.</summary>
public record WorkTask(Guid ItemId, DateTime? StartUtc, DateTime DeadlineUtc, double EffortHours);

/// <summary>One local day of a person's workload: the hours they have, the hours of work planned on it, and the ratio.</summary>
public record DayLoad(DateTime Date, double CapacityHours, double LoadHours)
{
    /// <summary>Load as a percentage of capacity; null when there is neither; 999 when there is work but no capacity (e.g. a weekend or a day off).</summary>
    public double? Percent => CapacityHours > 0 ? LoadHours / CapacityHours * 100 : LoadHours > 0 ? WorkloadService.NoCapacityPercent : null;
}

/// <summary>
/// A simple, explainable model of how loaded a person is. Capacity: 8 hours on a weekday, 0 on a weekend, minus the time they are out
/// of office or in a meeting (focus time does not reduce it - it is working time). Load: the effort of each open task is spread over
/// the days from today (or its planned start) to its deadline, in proportion to each day's capacity; an overdue task counts today.
/// Pure functions, no database; the caller decides which tasks belong to the person (and splits the effort of shared tasks).
/// </summary>
public static class WorkloadService
{
    public const double DayCapacityHours = 8;
    public const double NoCapacityPercent = 999;
    public const double LightBelowPercent = 60;
    public const double OverloadedAbovePercent = 100;

    /// <summary>A task's effort is never spread over more days than this (a deadline a year away would make every day look empty).</summary>
    public const int MaxSpreadDays = 120;

    /// <summary>The load of every day of the window <paramref name="from"/> .. <paramref name="from"/> + <paramref name="days"/> - 1.</summary>
    public static IReadOnlyList<DayLoad> Compute(
        DateTime from, int days, TimeZoneInfo zone, DateTime nowUtc, IReadOnlyList<TimeBlock> blocks, IReadOnlyList<WorkTask> tasks)
    {
        var firstDay = from.Date;
        var today = LocalDate(nowUtc, zone);
        var capacityCache = new Dictionary<DateTime, double>();

        double CapacityOn(DateTime day)
        {
            if (!capacityCache.TryGetValue(day, out var capacity))
            {
                capacity = CapacityOfDay(day, zone, blocks);
                capacityCache[day] = capacity;
            }

            return capacity;
        }

        var load = new Dictionary<DateTime, double>();
        foreach (var task in tasks.Where(t => t.EffortHours > 0))
        {
            var startDay = task.StartUtc is { } s ? Max(LocalDate(s, zone), today) : today;
            var endDay = LocalDate(task.DeadlineUtc, zone);
            if (endDay < startDay)
            {
                endDay = startDay; // overdue (or planned to start after its deadline): it weighs on its first day
            }
            if ((endDay - startDay).TotalDays >= MaxSpreadDays)
            {
                endDay = startDay.AddDays(MaxSpreadDays - 1);
            }

            var span = Enumerable.Range(0, (int)(endDay - startDay).TotalDays + 1).Select(i => startDay.AddDays(i)).ToList();
            var totalCapacity = span.Sum(CapacityOn);
            foreach (var day in span)
            {
                // Proportional to the day's capacity; when no day has any (all weekend / out of office) the work lands on the last day.
                var share = totalCapacity > 0 ? CapacityOn(day) / totalCapacity : day == endDay ? 1.0 : 0.0;
                if (share > 0)
                {
                    load[day] = load.GetValueOrDefault(day) + task.EffortHours * share;
                }
            }
        }

        return Enumerable.Range(0, days)
            .Select(i => firstDay.AddDays(i))
            .Select(day => new DayLoad(day, day < today ? 0 : CapacityOn(day), day < today ? 0 : load.GetValueOrDefault(day)))
            .ToList();
    }

    /// <summary>Total load of the days as one percentage (null when there is no work and no capacity at all).</summary>
    public static double? WindowPercent(IReadOnlyList<DayLoad> days)
    {
        var capacity = days.Sum(d => d.CapacityHours);
        var load = days.Sum(d => d.LoadHours);
        return capacity > 0 ? load / capacity * 100 : load > 0 ? NoCapacityPercent : null;
    }

    public static LoadLevel LevelOf(double? percent) =>
        percent is null || percent < LightBelowPercent ? LoadLevel.Light
        : percent <= OverloadedAbovePercent ? LoadLevel.Balanced
        : LoadLevel.Overloaded;

    /// <summary>The moment a task's deadline really falls: an all-day deadline is the END of that day in the person's zone.</summary>
    public static DateTime RealDeadlineUtc(DateTime storedDeadlineUtc, bool isAllDay, TimeZoneInfo zone) =>
        isAllDay ? AllDayTime.StartOfLocalDayUtc(storedDeadlineUtc.Date.AddDays(1), zone).AddTicks(-1) : storedDeadlineUtc;

    /// <summary>The moment a planned start really falls: an all-day start is the BEGINNING of that day in the person's zone.</summary>
    public static DateTime RealStartUtc(DateTime storedStartUtc, bool isAllDay, TimeZoneInfo zone) =>
        isAllDay ? AllDayTime.StartOfLocalDayUtc(storedStartUtc, zone) : storedStartUtc;

    /// <summary>Hours the person can work on a day: 8 on a weekday minus absences and meetings, 0 on a weekend.</summary>
    private static double CapacityOfDay(DateTime day, TimeZoneInfo zone, IReadOnlyList<TimeBlock> blocks)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return 0;
        }

        var start = AllDayTime.StartOfLocalDayUtc(day, zone);
        var end = AllDayTime.StartOfLocalDayUtc(day.AddDays(1), zone);
        var taken = AvailabilityService.HoursWithin(
            blocks.Where(b => b.Kind is BlockKind.OutOfOffice or BlockKind.Busy), zone, start, end);
        return Math.Max(0, DayCapacityHours - taken);
    }

    private static DateTime LocalDate(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).Date;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
