using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Notifications.Inbox;

/// <summary>Marks one inbox entry as read. Somebody else's entry looks exactly like a missing one (404), so ids cannot be probed.</summary>
public record MarkNotificationReadCommand(Guid UserId, Guid NotificationId) : IRequest;

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand>
{
    private readonly IApplicationDbContext _context;

    public MarkNotificationReadCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == request.NotificationId && n.UserId == request.UserId, cancellationToken)
            ?? throw new NotFoundException("Notification", request.NotificationId);

        if (notification.ReadAtUtc is null)
        {
            notification.ReadAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Marks the entries the user ticked in the app as read. Ids that are not the caller's are ignored, never reported.</summary>
public record MarkSelectedNotificationsReadCommand(Guid UserId, IReadOnlyList<Guid> NotificationIds) : IRequest;

public class MarkSelectedNotificationsReadCommandValidator : AbstractValidator<MarkSelectedNotificationsReadCommand>
{
    public MarkSelectedNotificationsReadCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NotificationIds).NotEmpty().Must(ids => ids.Count <= NotificationBatch.MaxIds)
            .WithMessage($"At most {NotificationBatch.MaxIds} notifications at once.");
    }
}

public class MarkSelectedNotificationsReadCommandHandler : IRequestHandler<MarkSelectedNotificationsReadCommand>
{
    private readonly IApplicationDbContext _context;

    public MarkSelectedNotificationsReadCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(MarkSelectedNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        var ids = request.NotificationIds.Distinct().ToList();
        var unread = await _context.Notifications
            .Where(n => n.UserId == request.UserId && n.ReadAtUtc == null && ids.Contains(n.Id))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.ReadAtUtc = now;
        }

        if (unread.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>"Mark all as read" — every unread entry of the caller.</summary>
public record MarkAllNotificationsReadCommand(Guid UserId) : IRequest;

public class MarkAllNotificationsReadCommandHandler : IRequestHandler<MarkAllNotificationsReadCommand>
{
    private readonly IApplicationDbContext _context;

    public MarkAllNotificationsReadCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        var unread = await _context.Notifications
            .Where(n => n.UserId == request.UserId && n.ReadAtUtc == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.ReadAtUtc = now;
        }

        if (unread.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
