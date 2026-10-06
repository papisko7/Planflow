using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Teams.Common;

/// <summary>
/// Every user owns exactly one hidden "personal" team, so tasks never have to belong to a shared team.
/// It is created lazily (register, login, token refresh), which also covers accounts that predate the feature.
/// </summary>
public static class PersonalTeam
{
    public const string DefaultName = "Personal";

    /// <summary>Creates the user's personal team if missing; does NOT save — the caller's SaveChanges persists it.</summary>
    public static async Task EnsureAsync(IApplicationDbContext context, Guid userId, CancellationToken cancellationToken)
    {
        var exists = await context.TeamMembers
            .AnyAsync(m => m.UserId == userId && m.Team.IsPersonal, cancellationToken);
        if (exists)
        {
            return;
        }

        var team = new Team { Name = DefaultName, IsPersonal = true };
        context.Teams.Add(team);
        context.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = userId, Role = TeamRole.Owner });
    }
}
