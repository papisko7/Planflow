using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Notifications;

/// <summary>Small read helpers that fetch the names an e-mail needs ("Anna deleted your task in Team X").</summary>
internal static class NotificationLookups
{
    public static async Task<string> UserNameAsync(IApplicationDbContext context, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return "Someone";
        }

        return await context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Someone";
    }

    public static async Task<string> TeamNameAsync(IApplicationDbContext context, Guid teamId, CancellationToken cancellationToken) =>
        await context.Teams.AsNoTracking()
            .Where(t => t.Id == teamId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "your team";
}
