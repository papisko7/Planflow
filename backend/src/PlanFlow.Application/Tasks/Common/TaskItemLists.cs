using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Which of the team's own lists an item may sit in. The list must exist in the item's team; a task goes in a task list and an event in
/// a meeting list. When an edit changes the item's type (a task turned into an event) the old list no longer fits and the item simply
/// leaves it, the same way a status that is illegal for the new type falls back to the default.
/// </summary>
public static class TaskItemLists
{
    /// <returns>The list id to store: the requested one, or null when none was asked for or it does not fit the item's type.</returns>
    public static async Task<Guid?> ResolveAsync(
        IApplicationDbContext context, Guid teamId, TaskType type, Guid? requestedListId, CancellationToken cancellationToken)
    {
        if (requestedListId is null)
        {
            return null;
        }

        var list = await context.ItemLists.AsNoTracking()
            .Where(l => l.Id == requestedListId && l.TeamId == teamId)
            .Select(l => new { l.Kind })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException(new[] { new ValidationFailure("ItemListId", "The list must exist in the same team.") });

        return list.Kind == ItemList.KindFor(type) ? requestedListId : null;
    }
}
