using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Calendar.Queries.GetGoogleCalendarStatus;

public class GetGoogleCalendarStatusQueryHandler : IRequestHandler<GetGoogleCalendarStatusQuery, GoogleCalendarStatusDto>
{
    private readonly IApplicationDbContext _context;

    public GetGoogleCalendarStatusQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GoogleCalendarStatusDto> Handle(GetGoogleCalendarStatusQuery request, CancellationToken cancellationToken)
    {
        var integration = await _context.CalendarIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == request.UserId && c.Provider == CalendarProvider.Google && c.IsActive, cancellationToken);

        return integration is null
            ? new GoogleCalendarStatusDto(false, null, null)
            : new GoogleCalendarStatusDto(
                true, integration.ExternalAccountEmail, integration.LastSyncedAtUtc, integration.HasTasksScope, integration.LastTasksSyncedAtUtc);
    }
}
