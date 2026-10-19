using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Tasks.Commands.DeleteTask;

public class DeleteTaskCommandHandler : IRequestHandler<DeleteTaskCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly INotificationService _notifications;

    public DeleteTaskCommandHandler(IApplicationDbContext context, ICacheService cache, INotificationService notifications)
    {
        _context = context;
        _cache = cache;
        _notifications = notifications;
    }

    public async Task Handle(DeleteTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _context.CalendarItems
            .Include(t => t.Reminders)
            .Include(t => t.Participants)
            .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken)
            ?? throw new NotFoundException(nameof(CalendarItem), request.TaskId);

        // The guests / co-contractors are told too; their rows go with the task (cascade), so they are read before the delete.
        var participantIds = await _context.ItemParticipants.AsNoTracking()
            .Where(p => p.CalendarItemId == task.Id)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

        // TaskHistory/UrgencyScoreLog rows cascade-delete with the task (see TaskHistoryConfiguration),
        // so no separate "Deleted" audit entry is written here — it would be removed in the same transaction.
        // An item that repeats may take other occurrences with it (the pop-up's choice); a Member only deletes the ones assigned to them.
        var nowUtc = DateTime.UtcNow;
        await SeriesEditing.DeleteAsync(
            _context, task, request.Scope, row => request.CanDeleteAny || row.AssignedUserId == request.DeletedByUserId, nowUtc, cancellationToken);

        await TaskNotifications.OnDeletedAsync(_context, _notifications, task, request.DeletedByUserId, participantIds, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        // Tasks it blocked are now free (the database clears their blocker) and its blocker no longer inherits
        // urgency from it, so the rest of the team is rescored after the delete is committed.
        var changedTaskIds = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            _context, task.TeamId, DateTime.UtcNow, ScoreTriggerSource.ManualUpdate, null, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        await TeamUrgencyRecalculator.InvalidateCachesAsync(_cache, task.TeamId, changedTaskIds.Append(task.Id), cancellationToken);
    }
}
