using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Users.Commands.UpdateCalendarSharing;

public class UpdateCalendarSharingCommandHandler : IRequestHandler<UpdateCalendarSharingCommand, Unit>
{
    private readonly IApplicationDbContext _context;

    public UpdateCalendarSharingCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Unit> Handle(UpdateCalendarSharingCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        if (user.CalendarSharingLevel != request.Level)
        {
            user.CalendarSharingLevel = request.Level;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
