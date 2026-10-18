using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// The time zones of the people whose all-day items are being scored. A zone only matters for an all-day item (it decides when
/// its day begins and ends), so only the assignees of such items are looked up; everyone else is scored in UTC as before.
/// </summary>
public static class UserTimeZones
{
    public static async Task<IReadOnlyDictionary<Guid, TimeZoneInfo>> LoadAsync(
        IApplicationDbContext context, IEnumerable<CalendarItem> items, CancellationToken cancellationToken)
    {
        var userIds = items
            .Where(i => i.IsAllDay && i.AssignedUserId is not null)
            .Select(i => i.AssignedUserId!.Value)
            .Distinct()
            .ToList();
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, TimeZoneInfo>();
        }

        var rows = await context.Users
            .Where(u => userIds.Contains(u.Id) && u.TimeZoneId != null)
            .Select(u => new { u.Id, u.TimeZoneId })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => AllDayTime.ZoneOrUtc(r.TimeZoneId));
    }

    /// <summary>The zone in which an item's all-day days are read: its assignee's, or UTC when unassigned / unknown.</summary>
    public static Func<CalendarItem, TimeZoneInfo> ResolverFor(IReadOnlyDictionary<Guid, TimeZoneInfo>? zones) =>
        item => item.AssignedUserId is { } id && zones is not null && zones.TryGetValue(id, out var zone) ? zone : TimeZoneInfo.Utc;
}
