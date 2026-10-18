using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Calendar.Common;

/// <summary>Where the Google sync jobs put what they import. One rule for Calendar and Tasks, so both land in the same place.</summary>
public static class SyncDestination
{
    /// <summary>
    /// Imported items need a TeamId (items are team-owned), but a Google connection is per-user. Calendar events and tasks are personal,
    /// so they go into the user's personal team; an account without one falls back to the earliest team they joined. Null = no team at all.
    /// </summary>
    public static Task<Guid?> FindTeamIdAsync(IApplicationDbContext context, Guid userId, CancellationToken cancellationToken) =>
        context.TeamMembers
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.Team.IsPersonal)
            .ThenBy(m => m.JoinedAtUtc)
            .Select(m => (Guid?)m.TeamId)
            .FirstOrDefaultAsync(cancellationToken);
}
