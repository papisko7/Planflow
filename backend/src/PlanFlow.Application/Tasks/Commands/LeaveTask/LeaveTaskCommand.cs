using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Tasks.Commands.LeaveTask;

/// <summary>A guest of an event / co-contractor of a task steps out of it. Only their own participation can be ended this way.</summary>
public record LeaveTaskCommand(Guid TaskId, Guid UserId) : IRequest;

public class LeaveTaskCommandHandler : IRequestHandler<LeaveTaskCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly INotificationService _notifications;

    public LeaveTaskCommandHandler(IApplicationDbContext context, ICacheService cache, INotificationService notifications)
    {
        _context = context;
        _cache = cache;
        _notifications = notifications;
    }

    public async Task Handle(LeaveTaskCommand request, CancellationToken cancellationToken)
    {
        var participant = await _context.ItemParticipants
            .Include(p => p.CalendarItem)
            .FirstOrDefaultAsync(p => p.CalendarItemId == request.TaskId && p.UserId == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(ItemParticipant), request.UserId);

        var task = participant.CalendarItem;
        _context.ItemParticipants.Remove(participant);
        _context.TaskHistories.Add(new TaskHistory
        {
            CalendarItemId = task.Id,
            ChangedByUserId = request.UserId,
            ChangeType = TaskChangeType.FieldUpdated,
            FieldName = nameof(CalendarItem.Participants),
            OldValue = request.UserId.ToString()
        });

        // The one person in charge hears that somebody stepped out (as an edit of their item, so the "task changes" switch decides).
        if (task.AssignedUserId is { } assignee)
        {
            var who = await NotificationLookups.UserNameAsync(_context, request.UserId, cancellationToken);
            await _notifications.NotifyAsync(
                NotificationEvent.TaskChanged, new[] { assignee },
                EmailTemplates.TaskChanged(
                    task.Title,
                    await NotificationLookups.TeamNameAsync(_context, task.TeamId, cancellationToken),
                    who,
                    new[] { $"{who} is no longer a {EmailTemplates.RoleWord(task.TaskType)}" },
                    task.TaskType),
                task.TeamId, request.UserId, cancellationToken, task.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.TeamTasks(task.TeamId), cancellationToken);
    }
}
