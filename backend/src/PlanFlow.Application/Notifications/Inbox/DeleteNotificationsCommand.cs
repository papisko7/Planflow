using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Notifications.Inbox;

/// <summary>Most entries one request may touch. The app shows 30 per page, so this only guards against absurd bodies.</summary>
public static class NotificationBatch
{
    public const int MaxIds = 200;
}

/// <summary>Removes one inbox entry for good. Somebody else's entry looks exactly like a missing one (404), so ids cannot be probed.</summary>
public record DeleteNotificationCommand(Guid UserId, Guid NotificationId) : IRequest;

public class DeleteNotificationCommandHandler : IRequestHandler<DeleteNotificationCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteNotificationCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteNotificationCommand request, CancellationToken cancellationToken)
    {
        // The user picked this very entry, so it goes even when its push is still queued - deleting it also cancels that push.
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == request.NotificationId && n.UserId == request.UserId, cancellationToken)
            ?? throw new NotFoundException("Notification", request.NotificationId);

        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Removes the entries the user ticked in the app. Ids that are not the caller's are ignored, never reported.</summary>
public record DeleteSelectedNotificationsCommand(Guid UserId, IReadOnlyList<Guid> NotificationIds) : IRequest;

public class DeleteSelectedNotificationsCommandValidator : AbstractValidator<DeleteSelectedNotificationsCommand>
{
    public DeleteSelectedNotificationsCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NotificationIds).NotEmpty().Must(ids => ids.Count <= NotificationBatch.MaxIds)
            .WithMessage($"At most {NotificationBatch.MaxIds} notifications at once.");
    }
}

public class DeleteSelectedNotificationsCommandHandler : IRequestHandler<DeleteSelectedNotificationsCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteSelectedNotificationsCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteSelectedNotificationsCommand request, CancellationToken cancellationToken)
    {
        var ids = request.NotificationIds.Distinct().ToList();
        var selected = await _context.Notifications
            .Where(n => n.UserId == request.UserId && ids.Contains(n.Id))
            .ToListAsync(cancellationToken);

        if (selected.Count > 0)
        {
            _context.Notifications.RemoveRange(selected);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// "Clear all": empties the caller's inbox, except entries whose push has not left the server yet. The user never saw those
/// (the push is how they find out), so wiping them would silently swallow a notification; they stay and can be cleared next time.
/// </summary>
public record ClearNotificationsCommand(Guid UserId) : IRequest;

public class ClearNotificationsCommandHandler : IRequestHandler<ClearNotificationsCommand>
{
    private readonly IApplicationDbContext _context;

    public ClearNotificationsCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(ClearNotificationsCommand request, CancellationToken cancellationToken)
    {
        var removable = await _context.Notifications
            .Where(n => n.UserId == request.UserId && n.PushState != PushDeliveryState.Pending)
            .ToListAsync(cancellationToken);

        if (removable.Count > 0)
        {
            _context.Notifications.RemoveRange(removable);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
