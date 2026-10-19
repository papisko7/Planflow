using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Users.Common;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Users.Queries.GetProfile;

public class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, UserProfileDto>
{
    private readonly IApplicationDbContext _context;

    public GetProfileQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UserProfileDto> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        return new UserProfileDto(user.Id, user.Email, user.DisplayName, user.IsEmailVerified, user.TimeZoneId, user.CalendarSharingLevel);
    }
}
