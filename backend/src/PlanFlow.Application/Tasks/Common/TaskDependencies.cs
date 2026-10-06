using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Tasks.Common;

public static class TaskDependencies
{
    /// <summary>
    /// A task may only be blocked by another task of the same team, never by itself, and the chain of blockers
    /// must not loop back (A blocked by B blocked by A) — a loop would mean neither could ever start.
    /// </summary>
    /// <param name="taskId">The task being edited, or null when it is being created (a new task can't be part of a loop).</param>
    public static async Task EnsureValidBlockerAsync(
        IApplicationDbContext context, Guid teamId, Guid? taskId, Guid? blockerId, CancellationToken cancellationToken)
    {
        if (blockerId is null)
        {
            return;
        }

        if (blockerId == taskId)
        {
            throw Invalid("A task cannot be blocked by itself.");
        }

        var blocker = await context.Tasks
            .AsNoTracking()
            .Where(t => t.Id == blockerId && t.TeamId == teamId)
            .Select(t => new { t.BlockedByTaskId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw Invalid("The blocking task must exist in the same team.");

        // Walk up the chain of blockers; meeting the edited task again means the new link would close a loop.
        var visited = new HashSet<Guid> { blockerId.Value };
        var next = blocker.BlockedByTaskId;
        while (next is { } currentId && visited.Add(currentId))
        {
            if (currentId == taskId)
            {
                throw Invalid("This dependency would create a cycle of blocking tasks.");
            }

            next = await context.Tasks
                .AsNoTracking()
                .Where(t => t.Id == currentId)
                .Select(t => t.BlockedByTaskId)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }

    private static ValidationException Invalid(string message) =>
        new(new[] { new ValidationFailure("BlockedByTaskId", message) });
}
