using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Application.Tasks.Commands.BulkTasks;

/// <summary>
/// What a bulk action did: how many items it changed and which ones it left alone (not in the team, not the caller's to change, or a
/// status the item's type does not have). Skipping is not an error: the rest of the selection is still processed.
/// </summary>
public record BulkTaskResult(int Succeeded, IReadOnlyList<Guid> SkippedIds);

/// <summary>Deletes many items of one team in one go (selection mode and "Clear"). One urgency recalculation for the whole batch.</summary>
/// <param name="CanActOnAny">Owner/Admin may touch any item; everybody else only the ones assigned to them.</param>
public record BulkDeleteTasksCommand(Guid TeamId, IReadOnlyList<Guid> TaskIds, Guid ActorUserId, bool CanActOnAny) : IRequest<BulkTaskResult>;

/// <summary>Sets one status on many items. An item whose type does not have that status (Completed on an event) is skipped, not converted.</summary>
public record BulkSetStatusCommand(Guid TeamId, IReadOnlyList<Guid> TaskIds, TaskStatus Status, Guid ActorUserId, bool CanActOnAny) : IRequest<BulkTaskResult>;

/// <summary>Puts many items into one of the team's own lists, or takes them out of any list (<paramref name="ItemListId"/> null). A task fits only a task list, an event a meeting list.</summary>
public record BulkMoveToListCommand(Guid TeamId, IReadOnlyList<Guid> TaskIds, Guid? ItemListId, Guid ActorUserId, bool CanActOnAny) : IRequest<BulkTaskResult>;

public static class BulkTaskLimits
{
    /// <summary>One request carries at most this many ids (the app sends a bigger selection in several requests).</summary>
    public const int MaxIds = 1000;
}

public class BulkDeleteTasksCommandValidator : AbstractValidator<BulkDeleteTasksCommand>
{
    public BulkDeleteTasksCommandValidator()
    {
        RuleFor(x => x.TaskIds).NotEmpty().Must(ids => ids.Count <= BulkTaskLimits.MaxIds).WithMessage($"At most {BulkTaskLimits.MaxIds} items per request.");
    }
}

public class BulkSetStatusCommandValidator : AbstractValidator<BulkSetStatusCommand>
{
    public BulkSetStatusCommandValidator()
    {
        RuleFor(x => x.TaskIds).NotEmpty().Must(ids => ids.Count <= BulkTaskLimits.MaxIds).WithMessage($"At most {BulkTaskLimits.MaxIds} items per request.");
        RuleFor(x => x.Status).IsInEnum();
    }
}

public class BulkMoveToListCommandValidator : AbstractValidator<BulkMoveToListCommand>
{
    public BulkMoveToListCommandValidator()
    {
        RuleFor(x => x.TaskIds).NotEmpty().Must(ids => ids.Count <= BulkTaskLimits.MaxIds).WithMessage($"At most {BulkTaskLimits.MaxIds} items per request.");
    }
}

/// <summary>The part the three handlers share: finding the items and deciding which of them the caller may change.</summary>
internal static class BulkTaskSupport
{
    /// <summary>The requested items that exist in the team, split into the ones the caller may change and everything else (skipped).</summary>
    public static async Task<(List<CalendarItem> Allowed, List<Guid> Skipped)> LoadAsync(
        IApplicationDbContext context, Guid teamId, IReadOnlyList<Guid> requestedIds, Guid actorUserId, bool canActOnAny,
        CancellationToken cancellationToken)
    {
        var ids = requestedIds.Distinct().ToList();
        var found = await context.CalendarItems.Where(t => t.TeamId == teamId && ids.Contains(t.Id)).ToListAsync(cancellationToken);

        var allowed = found.Where(t => canActOnAny || t.AssignedUserId == actorUserId).ToList();
        var allowedIds = allowed.Select(t => t.Id).ToHashSet();
        return (allowed, ids.Where(id => !allowedIds.Contains(id)).ToList());
    }

    /// <summary>The guests / co-contractors of the given items, by item, so the right people hear about a change.</summary>
    public static async Task<Dictionary<Guid, List<Guid>>> ParticipantsOfAsync(
        IApplicationDbContext context, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        var rows = await context.ItemParticipants.AsNoTracking()
            .Where(p => itemIds.Contains(p.CalendarItemId))
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new { p.CalendarItemId, p.UserId })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.CalendarItemId).ToDictionary(g => g.Key, g => g.Select(r => r.UserId).ToList());
    }

    /// <summary>True when somebody besides the actor would be told about a change to the item (nobody is told about their own action).</summary>
    public static bool SomeoneElseCares(CalendarItem item, IReadOnlyCollection<Guid> participantIds, Guid actorUserId) =>
        (item.AssignedUserId is { } assignee && assignee != actorUserId) || participantIds.Any(id => id != actorUserId);
}

public class BulkDeleteTasksCommandHandler : IRequestHandler<BulkDeleteTasksCommand, BulkTaskResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly INotificationService _notifications;

    public BulkDeleteTasksCommandHandler(IApplicationDbContext context, ICacheService cache, INotificationService notifications)
    {
        _context = context;
        _cache = cache;
        _notifications = notifications;
    }

    public async Task<BulkTaskResult> Handle(BulkDeleteTasksCommand request, CancellationToken cancellationToken)
    {
        var (allowed, skipped) = await BulkTaskSupport.LoadAsync(
            _context, request.TeamId, request.TaskIds, request.ActorUserId, request.CanActOnAny, cancellationToken);
        if (allowed.Count == 0)
        {
            return new BulkTaskResult(0, skipped);
        }

        var participants = await BulkTaskSupport.ParticipantsOfAsync(_context, allowed.Select(t => t.Id).ToList(), cancellationToken);

        foreach (var task in allowed)
        {
            var participantIds = participants.GetValueOrDefault(task.Id) ?? [];
            _context.CalendarItems.Remove(task); // history, score logs, reminders and participants go with it (cascade)
            if (BulkTaskSupport.SomeoneElseCares(task, participantIds, request.ActorUserId))
            {
                await TaskNotifications.OnDeletedAsync(_context, _notifications, task, request.ActorUserId, participantIds, cancellationToken);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Whatever the removed items blocked is free again and their blockers no longer inherit urgency from them: the team is rescored
        // once for the whole batch, not once per item.
        var changedTaskIds = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            _context, request.TeamId, DateTime.UtcNow, ScoreTriggerSource.ManualUpdate, null, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await TeamUrgencyRecalculator.InvalidateCachesAsync(_cache, request.TeamId, changedTaskIds.Concat(allowed.Select(t => t.Id)), cancellationToken);

        return new BulkTaskResult(allowed.Count, skipped);
    }
}

public class BulkSetStatusCommandHandler : IRequestHandler<BulkSetStatusCommand, BulkTaskResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly INotificationService _notifications;

    public BulkSetStatusCommandHandler(IApplicationDbContext context, ICacheService cache, INotificationService notifications)
    {
        _context = context;
        _cache = cache;
        _notifications = notifications;
    }

    public async Task<BulkTaskResult> Handle(BulkSetStatusCommand request, CancellationToken cancellationToken)
    {
        var (allowed, skipped) = await BulkTaskSupport.LoadAsync(
            _context, request.TeamId, request.TaskIds, request.ActorUserId, request.CanActOnAny, cancellationToken);

        // A status the item's type does not have is skipped rather than turned into the default: "Completed" on an event must not become "Confirmed".
        var fitting = allowed.Where(t => TaskStatusRules.IsValidFor(t.TaskType, request.Status)).ToList();
        skipped.AddRange(allowed.Where(t => !fitting.Contains(t)).Select(t => t.Id));
        if (fitting.Count == 0)
        {
            return new BulkTaskResult(0, skipped);
        }

        var participants = await BulkTaskSupport.ParticipantsOfAsync(_context, fitting.Select(t => t.Id).ToList(), cancellationToken);
        var nowUtc = DateTime.UtcNow;

        foreach (var task in fitting.Where(t => t.Status != request.Status))
        {
            var before = TaskSnapshot.Of(task);
            task.Status = request.Status;
            task.UpdatedAtUtc = nowUtc;
            _context.TaskHistories.Add(new TaskHistory
            {
                CalendarItemId = task.Id,
                ChangedByUserId = request.ActorUserId,
                ChangeType = TaskChangeType.StatusChanged
            });

            var participantIds = participants.GetValueOrDefault(task.Id) ?? [];
            if (BulkTaskSupport.SomeoneElseCares(task, participantIds, request.ActorUserId))
            {
                await TaskNotifications.OnUpdatedAsync(
                    _context, _notifications, task, before, request.ActorUserId, ParticipantChanges.None, participantIds, cancellationToken);
            }
        }

        // Finished items leave the ranking and reopened ones return to it, and the neighbours of both change: one pass for the team.
        var changedTaskIds = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            _context, request.TeamId, nowUtc, ScoreTriggerSource.ManualUpdate, null, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await TeamUrgencyRecalculator.InvalidateCachesAsync(_cache, request.TeamId, changedTaskIds.Concat(fitting.Select(t => t.Id)), cancellationToken);

        return new BulkTaskResult(fitting.Count, skipped);
    }
}

public class BulkMoveToListCommandHandler : IRequestHandler<BulkMoveToListCommand, BulkTaskResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public BulkMoveToListCommandHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<BulkTaskResult> Handle(BulkMoveToListCommand request, CancellationToken cancellationToken)
    {
        var (allowed, skipped) = await BulkTaskSupport.LoadAsync(
            _context, request.TeamId, request.TaskIds, request.ActorUserId, request.CanActOnAny, cancellationToken);

        ItemListKind? listKind = null;
        if (request.ItemListId is { } listId)
        {
            listKind = await _context.ItemLists.AsNoTracking()
                .Where(l => l.Id == listId && l.TeamId == request.TeamId)
                .Select(l => (ItemListKind?)l.Kind)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new FluentValidation.ValidationException(
                    new[] { new FluentValidation.Results.ValidationFailure("ItemListId", "The list must exist in the same team.") });
        }

        // A task belongs in a task list and an event in a meeting list; the others of the selection are left where they are.
        var fitting = allowed.Where(t => listKind is null || ItemList.KindFor(t.TaskType) == listKind).ToList();
        skipped.AddRange(allowed.Where(t => !fitting.Contains(t)).Select(t => t.Id));

        var nowUtc = DateTime.UtcNow;
        foreach (var task in fitting.Where(t => t.ItemListId != request.ItemListId))
        {
            task.ItemListId = request.ItemListId;
            task.UpdatedAtUtc = nowUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.TeamTasks(request.TeamId), cancellationToken);

        return new BulkTaskResult(fitting.Count, skipped);
    }
}
