using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Users.Commands.UpdateTimeZone;

public class UpdateTimeZoneCommandHandler : IRequestHandler<UpdateTimeZoneCommand, Unit>
{
    private readonly IApplicationDbContext _context;

    public UpdateTimeZoneCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Unit> Handle(UpdateTimeZoneCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        if (user.TimeZoneId != request.TimeZoneId)
        {
            user.TimeZoneId = request.TimeZoneId;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
