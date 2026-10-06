using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Users.Common;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Users.Queries.GetNotificationPreferences;

public record GetNotificationPreferencesQuery(Guid UserId) : IRequest<NotificationPreferencesDto>;

public class GetNotificationPreferencesQueryHandler : IRequestHandler<GetNotificationPreferencesQuery, NotificationPreferencesDto>
{
    private readonly IApplicationDbContext _context;

    public GetNotificationPreferencesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<NotificationPreferencesDto> Handle(GetNotificationPreferencesQuery request, CancellationToken cancellationToken)
    {
        // A user who never opened the settings has no row yet and simply gets the defaults.
        var preferences = await _context.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, cancellationToken)
            ?? NotificationPreference.CreateDefault(request.UserId);

        return NotificationPreferencesDto.FromEntity(preferences);
    }
}
