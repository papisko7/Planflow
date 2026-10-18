using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>Who joined and who left an item in one save, so the right people can be told.</summary>
public record ParticipantChanges(IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed)
{
    public static readonly ParticipantChanges None = new([], []);
}

/// <summary>
/// The rules for the guests of an event and the co-contractors of a task, and the way they are saved. The assignee is not a participant:
/// they are the item's owner. Only team members can take part, and a personal workspace has nobody but its owner.
/// </summary>
public static class TaskParticipants
{
    /// <summary>
    /// The people who will take part in <paramref name="item"/> (as it will be stored), checked against the team. A kind of event without
    /// guests (Out of office, Focus time, ...) has none, like every field its template lacks. The assignee being in the list is not an error:
    /// handing the item to one of its participants simply makes them its owner.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> ResolveAsync(
        IApplicationDbContext context, CalendarItem item, IReadOnlyList<Guid> requested, CancellationToken cancellationToken)
    {
        if (item.TaskType == TaskType.Event && !EventTemplateRules.For(item.TaskType, item.EventTemplate).AllowsGuests)
        {
            return [];
        }

        if (requested.Distinct().Count() != requested.Count)
        {
            throw Invalid("The same person is listed twice.");
        }

        var people = requested.Where(id => id != item.AssignedUserId).ToList();
        if (people.Count == 0)
        {
            return people;
        }

        if (people.Count > ItemParticipant.MaxPerItem)
        {
            throw Invalid($"At most {ItemParticipant.MaxPerItem} people can take part besides the assignee.");
        }

        var team = await context.Teams.AsNoTracking()
            .Where(t => t.Id == item.TeamId)
            .Select(t => new { t.IsPersonal })
            .FirstOrDefaultAsync(cancellationToken);
        if (team is null || team.IsPersonal)
        {
            throw Invalid("A personal workspace has no other people to invite.");
        }

        var members = await context.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == item.TeamId && people.Contains(m.UserId))
            .CountAsync(cancellationToken);
        if (members != people.Count)
        {
            throw Invalid("Everyone invited must be a member of the item's team.");
        }

        return people;
    }

    /// <summary>
    /// Makes the participants of <paramref name="item"/> equal to <paramref name="wanted"/> and reports who joined and who left.
    /// Someone who stays is left alone, apart from the role, which follows the item's type (a guest of an event becomes a co-contractor
    /// when the event is turned into a task). The item must have been loaded with its <see cref="CalendarItem.Participants"/>.
    /// </summary>
    public static ParticipantChanges Replace(IApplicationDbContext context, CalendarItem item, IReadOnlyList<Guid> wanted)
    {
        var role = ItemParticipant.RoleFor(item.TaskType);

        var removed = item.Participants.Where(p => !wanted.Contains(p.UserId)).ToList();
        foreach (var participant in removed)
        {
            context.ItemParticipants.Remove(participant);
        }

        foreach (var participant in item.Participants.Where(p => wanted.Contains(p.UserId) && p.Role != role))
        {
            participant.Role = role;
            participant.UpdatedAtUtc = DateTime.UtcNow;
        }

        var storedIds = item.Participants.Select(p => p.UserId).ToHashSet();
        var added = wanted.Where(id => !storedIds.Contains(id)).ToList();
        foreach (var userId in added)
        {
            context.ItemParticipants.Add(new ItemParticipant { CalendarItemId = item.Id, UserId = userId, Role = role });
        }

        return new ParticipantChanges(added, removed.Select(p => p.UserId).ToList());
    }

    private static ValidationException Invalid(string message) =>
        new(new[] { new ValidationFailure("ParticipantUserIds", message) });
}
