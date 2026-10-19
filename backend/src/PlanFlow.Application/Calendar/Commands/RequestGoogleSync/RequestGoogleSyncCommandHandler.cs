using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Calendar.Commands.RequestGoogleSync;

/// <summary>
/// Google is fetched only when the user presses "Sync now", so this is the single gate in front of every sync. It requires a connected
/// account and refuses a repeat press within <see cref="MinInterval"/> of the last sync: Google limits requests, and a second sync seconds
/// later would find nothing new.
/// </summary>
public class RequestGoogleSyncCommandHandler : IRequestHandler<RequestGoogleSyncCommand, bool>
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

    private readonly IApplicationDbContext _context;

    public RequestGoogleSyncCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(RequestGoogleSyncCommand request, CancellationToken cancellationToken)
    {
        var integration = await _context.CalendarIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == request.UserId && c.Provider == CalendarProvider.Google && c.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.CalendarIntegration), request.UserId);

        return integration.LastSyncedAtUtc is not { } last || DateTime.UtcNow - last >= MinInterval;
    }
}
