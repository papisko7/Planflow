using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Calendar.Commands.DisconnectGoogleCalendar;

/// <summary>
/// Revokes a user's Google Calendar link. Soft-delete (<c>IsActive = false</c> + tokens wiped)
/// rather than a row delete, so <see cref="PlanFlow.Domain.Entities.CalendarIntegration.LastSyncedAtUtc"/>
/// and the sync history survive for audit/thesis-evaluation purposes.
/// </summary>
public class DisconnectGoogleCalendarCommandHandler : IRequestHandler<DisconnectGoogleCalendarCommand>
{
    private readonly IApplicationDbContext _context;

    public DisconnectGoogleCalendarCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DisconnectGoogleCalendarCommand request, CancellationToken cancellationToken)
    {
        var integration = await _context.CalendarIntegrations
            .FirstOrDefaultAsync(c => c.UserId == request.UserId && c.Provider == CalendarProvider.Google, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.CalendarIntegration), request.UserId);

        integration.IsActive = false;
        integration.EncryptedAccessToken = string.Empty;
        integration.EncryptedRefreshToken = string.Empty;
        integration.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
