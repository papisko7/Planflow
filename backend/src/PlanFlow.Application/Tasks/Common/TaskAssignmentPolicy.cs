using PlanFlow.Application.Common.Exceptions;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>Who may hand an item to whom, and what is stored about the hand-over.</summary>
/// <param name="AssignedByUserId">The Owner/Admin who gave the item to someone else; null when nobody did (taken by the person, unassigned).</param>
/// <param name="IsLocked">True when the assignee cannot give the item back (it was assigned by an Owner/Admin).</param>
public readonly record struct AssignmentOutcome(Guid? AssignedByUserId, bool IsLocked);

/// <summary>
/// The rules of assigning, in one pure place (they used to sit in the controller):
/// an Owner/Admin may give any item to anyone, take it off a person or give it to another; a Member may only keep an item that is theirs,
/// take an unassigned one, or give back one they took themselves. An item an Owner/Admin assigned is locked to its assignee: they
/// can work on it and invite co-contractors, but cannot drop it. The lock follows the role at the moment of assignment, so a later
/// demotion of the assigner changes nothing.
/// </summary>
public static class TaskAssignmentPolicy
{
    public const string LockedMessage = "This task was assigned to you by an Owner or Admin, so you cannot give it back. Ask them to reassign it.";
    public const string NotYoursMessage = "Only an Owner or Admin can change who a task is assigned to; you can only take an unassigned task or edit your own.";

    /// <summary>
    /// Decides what an edit that moves the assignee from <paramref name="currentAssignee"/> to <paramref name="requestedAssignee"/>
    /// means. Throws <see cref="ForbiddenAccessException"/> when the caller may not do it; otherwise returns what to store.
    /// </summary>
    public static AssignmentOutcome Decide(
        Guid? callerId, bool callerIsManager, Guid? currentAssignee, Guid? currentAssignedBy, bool currentLocked, Guid? requestedAssignee)
    {
        var unchanged = requestedAssignee == currentAssignee;

        if (callerIsManager)
        {
            if (unchanged)
            {
                return new AssignmentOutcome(currentAssignedBy, currentLocked);
            }

            // Giving work to somebody else locks it to them; taking it for oneself or clearing it does not.
            return requestedAssignee is { } assignee && assignee != callerId
                ? new AssignmentOutcome(callerId, true)
                : new AssignmentOutcome(null, false);
        }

        var isOwnTask = currentAssignee is not null && currentAssignee == callerId;
        if (isOwnTask && unchanged)
        {
            return new AssignmentOutcome(currentAssignedBy, currentLocked);
        }

        var isClaim = currentAssignee is null && requestedAssignee is not null && requestedAssignee == callerId;
        if (isClaim)
        {
            return new AssignmentOutcome(null, false);
        }

        var isAbandon = isOwnTask && requestedAssignee is null;
        if (isAbandon)
        {
            return currentLocked
                ? throw new ForbiddenAccessException(LockedMessage)
                : new AssignmentOutcome(null, false);
        }

        throw new ForbiddenAccessException(NotYoursMessage);
    }

    /// <summary>What to store for a brand-new item: locked only when an Owner/Admin hands it to somebody else.</summary>
    public static AssignmentOutcome ForNewItem(Guid? creatorId, bool creatorIsManager, Guid? assignee) =>
        creatorIsManager && assignee is { } id && id != creatorId
            ? new AssignmentOutcome(creatorId, true)
            : new AssignmentOutcome(null, false);
}
