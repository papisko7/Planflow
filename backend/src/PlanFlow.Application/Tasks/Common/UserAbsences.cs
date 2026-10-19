using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Availability.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// The out-of-office time of the people whose tasks are being scored (see <see cref="AbsenceDeadline"/>). Only the assignees of open tasks
/// with a future deadline are looked up, in one query, like <see cref="UserTimeZones"/>. What counts as an absence follows what the person
/// shares: an Out of office event of a team is team data, an imported Google one is read through <see cref="PersonalCalendarSharing"/>, so a
/// person who shares nothing (or marks the event private) never has their absence show up in a score everybody can read.
/// </summary>
public static class UserAbsences
{
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AbsenceSpan>>> LoadAsync(
        IApplicationDbContext context, IEnumerable<CalendarItem> items, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var userIds = items
            .Where(i => i.TaskType == TaskType.Task && i.AssignedUserId is not null && i.DeadlineUtc > nowUtc)
            .Select(i => i.AssignedUserId!.Value)
            .Distinct()
            .ToList();
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<AbsenceSpan>>();
        }

        // Yesterday on: an absence that began earlier and still runs has an end after this moment.
        var since = nowUtc.AddDays(-1);
        var events = await context.CalendarItems
            .AsNoTracking()
            .Where(i => i.TaskType == TaskType.Event
                && i.EventTemplate == EventTemplate.OutOfOffice
                && i.Status != Domain.Enums.TaskStatus.Cancelled
                && i.AssignedUserId != null && userIds.Contains(i.AssignedUserId.Value)
                && i.StartUtc != null && (i.EndUtc ?? i.StartUtc) >= since)
            .Select(i => new { Item = i, i.Team.IsPersonal })
            .ToListAsync(cancellationToken);
        if (events.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<AbsenceSpan>>();
        }

        var people = await context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.CalendarSharingLevel, u.TimeZoneId })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var result = new Dictionary<Guid, List<AbsenceSpan>>();
        foreach (var e in events)
        {
            var userId = e.Item.AssignedUserId!.Value;
            if (!people.TryGetValue(userId, out var person))
            {
                continue;
            }

            var block = e.IsPersonal && e.Item.ExternalCalendarEventId is not null
                ? PersonalCalendarSharing.BlockOf(e.Item, person.CalendarSharingLevel)
                : MemberCalendars.TeamBlockOf(e.Item);
            if (block is not { Kind: BlockKind.OutOfOffice })
            {
                continue;
            }

            var (start, end) = block.Interval(AllDayTime.ZoneOrUtc(person.TimeZoneId));
            if (!result.TryGetValue(userId, out var spans))
            {
                result[userId] = spans = new List<AbsenceSpan>();
            }
            spans.Add(new AbsenceSpan(start, end));
        }

        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<AbsenceSpan>)kv.Value);
    }

    /// <summary>The absences of an item's assignee, or null when it has none known.</summary>
    public static Func<CalendarItem, IReadOnlyList<AbsenceSpan>?> ResolverFor(IReadOnlyDictionary<Guid, IReadOnlyList<AbsenceSpan>>? absences) =>
        item => item.AssignedUserId is { } id && absences is not null && absences.TryGetValue(id, out var spans) ? spans : null;
}
