using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Services;

/// <summary>How available a person is. <see cref="NoData"/> = they share nothing, which is not the same as "free".</summary>
public enum MemberStatus
{
    NoData = 0,
    Free = 1,
    Busy = 2,
    FocusTime = 3,
    OutOfOffice = 4
}

/// <summary>Availability of one person over one local day.</summary>
public enum DayAvailability
{
    NoData = 0,
    Free = 1,
    /// <summary>Some time is taken, but most of the day is open.</summary>
    Partial = 2,
    Busy = 3,
    OutOfOffice = 4
}

/// <summary>
/// Turns the blocks of a person's calendar (already trimmed by <see cref="PersonalCalendarSharing"/>) into a status: "right now" for
/// the member cards and a verdict per day for the availability grid. Pure functions, no database.
/// </summary>
public static class AvailabilityService
{
    /// <summary>A day with at least this many hours out of office counts as an absence day (half of a standard working day).</summary>
    public const double AbsenceDayHours = 4;

    /// <summary>A day with at least this many busy hours counts as busy; fewer than <see cref="PartialFromHours"/> as free.</summary>
    public const double BusyDayHours = 6;

    public const double PartialFromHours = 1;

    /// <summary>The status at <paramref name="atUtc"/>. Out of office wins over a meeting, a meeting over focus time.</summary>
    public static MemberStatus StatusAt(CalendarSharingLevel level, IEnumerable<TimeBlock> blocks, TimeZoneInfo zone, DateTime atUtc)
    {
        if (level == CalendarSharingLevel.Off)
        {
            return MemberStatus.NoData;
        }

        var status = MemberStatus.Free;
        foreach (var block in blocks.Where(b => b.BlocksTime))
        {
            var (start, end) = block.Interval(zone);
            if (atUtc < start || atUtc >= end)
            {
                continue;
            }

            var candidate = block.Kind switch
            {
                BlockKind.OutOfOffice => MemberStatus.OutOfOffice,
                BlockKind.FocusTime => MemberStatus.FocusTime,
                _ => MemberStatus.Busy
            };
            status = Stronger(status, candidate);
        }

        return status;
    }

    /// <summary>The verdict for the local day <paramref name="literalDay"/> (a date, read literally) of a person in <paramref name="zone"/>.</summary>
    public static DayAvailability DayStatus(CalendarSharingLevel level, IEnumerable<TimeBlock> blocks, TimeZoneInfo zone, DateTime literalDay)
    {
        if (level == CalendarSharingLevel.Off)
        {
            return DayAvailability.NoData;
        }

        var dayStart = AllDayTime.StartOfLocalDayUtc(literalDay, zone);
        var dayEnd = AllDayTime.StartOfLocalDayUtc(literalDay.Date.AddDays(1), zone);

        var absenceHours = HoursWithin(blocks.Where(b => b.Kind == BlockKind.OutOfOffice), zone, dayStart, dayEnd);
        if (absenceHours >= AbsenceDayHours)
        {
            return DayAvailability.OutOfOffice;
        }

        var busyHours = HoursWithin(blocks.Where(b => b.BlocksTime), zone, dayStart, dayEnd);
        return busyHours >= BusyDayHours ? DayAvailability.Busy
            : busyHours >= PartialFromHours ? DayAvailability.Partial
            : DayAvailability.Free;
    }

    /// <summary>Hours of the window covered by at least one of the blocks (overlapping blocks are counted once). An all-day block counts as a full 8-hour day.</summary>
    public static double HoursWithin(IEnumerable<TimeBlock> blocks, TimeZoneInfo zone, DateTime windowStart, DateTime windowEnd)
    {
        var spans = new List<(DateTime Start, DateTime End)>();
        var allDayHours = 0.0;
        foreach (var block in blocks)
        {
            var (start, end) = block.Interval(zone);
            var from = start > windowStart ? start : windowStart;
            var to = end < windowEnd ? end : windowEnd;
            if (to <= from)
            {
                continue;
            }

            if (block.IsAllDay)
            {
                allDayHours = WorkloadService.DayCapacityHours; // the whole day is taken, whatever the timed blocks say
                continue;
            }

            spans.Add((from, to));
        }

        if (allDayHours > 0)
        {
            return allDayHours;
        }

        var total = TimeSpan.Zero;
        DateTime? currentEnd = null;
        DateTime currentStart = default;
        foreach (var span in spans.OrderBy(s => s.Start))
        {
            if (currentEnd is null)
            {
                currentStart = span.Start;
                currentEnd = span.End;
            }
            else if (span.Start <= currentEnd)
            {
                currentEnd = span.End > currentEnd ? span.End : currentEnd;
            }
            else
            {
                total += currentEnd.Value - currentStart;
                currentStart = span.Start;
                currentEnd = span.End;
            }
        }

        if (currentEnd is not null)
        {
            total += currentEnd.Value - currentStart;
        }

        return total.TotalHours;
    }

    private static MemberStatus Stronger(MemberStatus a, MemberStatus b) => Rank(b) > Rank(a) ? b : a;

    private static int Rank(MemberStatus s) => s switch
    {
        MemberStatus.OutOfOffice => 4,
        MemberStatus.Busy => 3,
        MemberStatus.FocusTime => 2,
        MemberStatus.Free => 1,
        _ => 0
    };
}
