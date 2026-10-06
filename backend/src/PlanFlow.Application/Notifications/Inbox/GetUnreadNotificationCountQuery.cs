using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Notifications.Inbox;

/// <summary>How many inbox entries the caller has not opened yet — the number on the bell.</summary>
public record GetUnreadNotificationCountQuery(Guid UserId) : IRequest<int>;

public class GetUnreadNotificationCountQueryHandler : IRequestHandler<GetUnreadNotificationCountQuery, int>
{
    private readonly IApplicationDbContext _context;

    public GetUnreadNotificationCountQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<int> Handle(GetUnreadNotificationCountQuery request, CancellationToken cancellationToken) =>
        _context.Notifications.CountAsync(n => n.UserId == request.UserId && n.ReadAtUtc == null, cancellationToken);
}
