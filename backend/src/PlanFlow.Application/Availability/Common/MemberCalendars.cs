using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Availability.Common;

/// <summary>An open task of a member: <see cref="EffortShareHours"/> is the member's own part when co-contractors share it equally.</summary>
public record MemberTaskLoad(
    Guid ItemId, string Title, DateTime? StartStoredUtc, DateTime DeadlineStoredUtc, bool IsAllDay, double? EffortHours,
    double? EffortShareHours, Domain.Enums.TaskStatus Status, double UrgencyScore);

/// <summary>Everything the availability views need about one member: who they are, what they share, their blocks and their tasks.</summary>
public record MemberCalendar(
    Guid UserId, string DisplayName, TeamRole Role, CalendarSharingLevel Level, TimeZoneInfo Zone, string? TimeZoneId,
    IReadOnlyList<TimeBlock> PersonalBlocks, IReadOnlyList<TimeBlock> TeamBlocks, IReadOnlyList<MemberTaskLoad> Tasks)
{
    /// <summary>Personal (already trimmed by the sharing level) and team blocks together.</summary>
    public IReadOnlyList<TimeBlock> AllBlocks => PersonalBlocks.Concat(TeamBlocks).ToList();
}

/// <summary>
/// Loads the calendars of the members of one team for a window of days. The caller must belong to the team; the personal calendar of a
/// member is read from their own personal team and trimmed by <see cref="PersonalCalendarSharing"/> before it leaves this class, and
/// work is counted from this team's tasks only (another team's tasks are not for this team to see).
/// </summary>
public static class MemberCalendars
{
    public const int MaxDays = 42;

    public static async Task<IReadOnlyList<MemberCalendar>> LoadAsync(
        IApplicationDbContext context, Guid teamId, Guid requestingUserId, Guid? onlyUserId, DateTime from, int days,
        CancellationToken cancellationToken)
    {
        var members = await context.TeamMembers
            .AsNoTracking()
            .Where(m => m.TeamId == teamId)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => new { m.UserId, m.Role, m.User.DisplayName, m.User.CalendarSharingLevel, m.User.TimeZoneId })
            .ToListAsync(cancellationToken);

        if (members.All(m => m.UserId != requestingUserId))
        {
            throw new ForbiddenAccessException("You are not a member of this team.");
        }

        if (onlyUserId is { } wanted)
        {
            members = members.Where(m => m.UserId == wanted).ToList();
            if (members.Count == 0)
            {
                throw new NotFoundException(nameof(TeamMember), wanted);
            }
        }

        var ids = members.Select(m => m.UserId).ToList();

        // One day of padding on both sides: an item's real moments depend on its owner's time zone, which can be up to ~14 hours off UTC.
        var windowStart = DateTime.SpecifyKind(from.Date.AddDays(-1), DateTimeKind.Utc);
        var windowEnd = DateTime.SpecifyKind(from.Date.AddDays(days + 1), DateTimeKind.Utc);

        var visibleIds = members.Where(m => m.CalendarSharingLevel != CalendarSharingLevel.Off).Select(m => m.UserId).ToList();

        // Personal calendar: imported events sitting in the member's own personal team, assigned to them.
        var personalItems = visibleIds.Count == 0
            ? new List<CalendarItem>()
            : await context.CalendarItems
                .AsNoTracking()
                .Where(i => i.Team.IsPersonal
                    && i.TaskType == TaskType.Event
                    && i.ExternalCalendarEventId != null
                    && i.Status != Domain.Enums.TaskStatus.Cancelled
                    && i.AssignedUserId != null && visibleIds.Contains(i.AssignedUserId.Value)
                    && i.StartUtc != null && i.StartUtc < windowEnd && (i.EndUtc ?? i.StartUtc) >= windowStart)
                .ToListAsync(cancellationToken);

        // This team's own events (shown in full - it is team data) and open tasks the member holds or shares.
        var teamEvents = await context.CalendarItems
            .AsNoTracking()
            .Where(i => i.TeamId == teamId
                && i.TaskType == TaskType.Event
                && i.Status != Domain.Enums.TaskStatus.Cancelled
                && i.StartUtc != null && i.StartUtc < windowEnd && (i.EndUtc ?? i.StartUtc) >= windowStart
                && ((i.AssignedUserId != null && ids.Contains(i.AssignedUserId.Value))
                    || i.Participants.Any(p => ids.Contains(p.UserId))))
            .Select(i => new { Item = i, People = i.Participants.Select(p => p.UserId).ToList() })
            .ToListAsync(cancellationToken);

        var teamTasks = await context.CalendarItems
            .AsNoTracking()
            .Where(i => i.TeamId == teamId
                && i.TaskType == TaskType.Task
                && (i.Status == Domain.Enums.TaskStatus.Backlog || i.Status == Domain.Enums.TaskStatus.InProgress)
                && i.DeadlineUtc != null
                && ((i.AssignedUserId != null && ids.Contains(i.AssignedUserId.Value))
                    || i.Participants.Any(p => ids.Contains(p.UserId))))
            .Select(i => new { Item = i, People = i.Participants.Select(p => p.UserId).ToList() })
            .ToListAsync(cancellationToken);

        return members.Select(m =>
        {
            var zone = AllDayTime.ZoneOrUtc(m.TimeZoneId);

            var personal = personalItems
                .Where(i => i.AssignedUserId == m.UserId)
                .Select(i => PersonalCalendarSharing.BlockOf(i, m.CalendarSharingLevel))
                .OfType<TimeBlock>()
                .OrderBy(b => b.StartUtc)
                .ToList();

            var team = teamEvents
                .Where(e => e.Item.AssignedUserId == m.UserId || e.People.Contains(m.UserId))
                .Select(e => TeamBlockOf(e.Item))
                .OfType<TimeBlock>()
                .OrderBy(b => b.StartUtc)
                .ToList();

            var tasks = teamTasks
                .Where(t => t.Item.AssignedUserId == m.UserId || t.People.Contains(m.UserId))
                .Select(t =>
                {
                    // The assignee and the co-contractors share the effort equally (the plan's default).
                    var people = t.People.Where(p => p != t.Item.AssignedUserId).Distinct().Count() + (t.Item.AssignedUserId is null ? 0 : 1);
                    double? effort = t.Item.EstimatedEffortHours is > 0 ? t.Item.EstimatedEffortHours : null;
                    return new MemberTaskLoad(
                        t.Item.Id, t.Item.Title, t.Item.StartUtc, t.Item.DeadlineUtc!.Value, t.Item.IsAllDay, effort,
                        effort is null ? null : effort / Math.Max(1, people), t.Item.Status, t.Item.CurrentUrgencyScore);
                })
                .OrderBy(t => t.DeadlineStoredUtc)
                .ToList();

            return new MemberCalendar(
                m.UserId, m.DisplayName, m.Role, m.CalendarSharingLevel, zone, m.TimeZoneId, personal, team, tasks);
        }).ToList();
    }

    /// <summary>Builds the availability row of one member for <paramref name="days"/> days starting at <paramref name="from"/>.</summary>
    public static MemberAvailabilityDto Summarize(MemberCalendar member, DateTime from, int days, DateTime nowUtc)
    {
        var blocks = member.AllBlocks;

        var workTasks = member.Tasks
            .Where(t => t.EffortShareHours is > 0)
            .Select(t => new WorkTask(
                t.ItemId,
                t.StartStoredUtc is { } s ? WorkloadService.RealStartUtc(s, t.IsAllDay, member.Zone) : null,
                WorkloadService.RealDeadlineUtc(t.DeadlineStoredUtc, t.IsAllDay, member.Zone),
                t.EffortShareHours!.Value))
            .ToList();

        var load = WorkloadService.Compute(from, days, member.Zone, nowUtc, blocks, workTasks);
        var perDay = load.Select(d => new DayAvailabilityDto(
            d.Date,
            AvailabilityService.DayStatus(member.Level, blocks, member.Zone, d.Date),
            d.CapacityHours,
            d.LoadHours,
            d.Percent,
            WorkloadService.LevelOf(d.Percent))).ToList();

        var windowPercent = WorkloadService.WindowPercent(load);
        var workingLocation = member.Level == CalendarSharingLevel.Details
            ? blocks.FirstOrDefault(b => b.Kind == BlockKind.WorkingLocation && NowWithin(b, member.Zone, nowUtc))?.Location
            : null;

        return new MemberAvailabilityDto(
            member.UserId,
            member.DisplayName,
            member.Role,
            member.Level,
            AvailabilityService.StatusAt(member.Level, blocks, member.Zone, nowUtc),
            workingLocation,
            windowPercent,
            WorkloadService.LevelOf(windowPercent),
            member.Tasks.Count,
            member.Tasks.Count(t => t.EffortHours is null),
            perDay);
    }

    private static bool NowWithin(TimeBlock block, TimeZoneInfo zone, DateTime nowUtc)
    {
        var (start, end) = block.Interval(zone);
        return nowUtc >= start && nowUtc < end;
    }

    private static TimeBlock? TeamBlockOf(CalendarItem item)
    {
        if (item.StartUtc is null)
        {
            return null;
        }

        var start = item.StartUtc.Value;
        var end = item.EndUtc is { } e && e > start ? e : start + (item.IsAllDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));
        var kind = item.EventTemplate switch
        {
            EventTemplate.OutOfOffice => BlockKind.OutOfOffice,
            EventTemplate.FocusTime => BlockKind.FocusTime,
            EventTemplate.WorkingLocation => BlockKind.WorkingLocation,
            EventTemplate.Birthday => BlockKind.Birthday,
            _ => item.IsBusy ? BlockKind.Busy : BlockKind.Free
        };
        return new TimeBlock(item.Id, kind, start, end, item.IsAllDay, item.Title, item.Location);
    }
}
