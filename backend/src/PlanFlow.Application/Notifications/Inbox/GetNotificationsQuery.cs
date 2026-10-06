using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Notifications.Inbox;

/// <summary>
/// The caller's inbox, newest first. Paged by time instead of page numbers: ask for entries older than <paramref name="Before"/>
/// (the CreatedAtUtc of the last entry you already have), so new arrivals never shift the pages.
/// </summary>
public record GetNotificationsQuery(Guid UserId, DateTime? Before, int Limit = 30) : IRequest<IReadOnlyList<NotificationDto>>;

public class GetNotificationsQueryValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, IReadOnlyList<NotificationDto>>
{
    private readonly IApplicationDbContext _context;

    public GetNotificationsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<NotificationDto>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Notifications.AsNoTracking().Where(n => n.UserId == request.UserId);
        if (request.Before is { } before)
        {
            query = query.Where(n => n.CreatedAtUtc < before);
        }

        var rows = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(request.Limit)
            .ToListAsync(cancellationToken);

        return rows.Select(NotificationDto.FromEntity).ToList();
    }
}
