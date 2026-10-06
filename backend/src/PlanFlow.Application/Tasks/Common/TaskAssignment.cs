using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Tasks.Common;

public static class TaskAssignment
{
    /// <summary>A task can only be handed to someone who actually belongs to the task's team.</summary>
    public static async Task EnsureAssigneeInTeamAsync(
        IApplicationDbContext context, Guid teamId, Guid? assigneeId, CancellationToken cancellationToken)
    {
        if (assigneeId is null)
        {
            return;
        }

        var isMember = await context.TeamMembers
            .AnyAsync(m => m.TeamId == teamId && m.UserId == assigneeId, cancellationToken);
        if (!isMember)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("AssignedUserId", "The assignee must be a member of the task's team.")
            });
        }
    }
}
