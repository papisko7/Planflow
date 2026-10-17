using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Application.Auth.Queries.IdentifyUser;

public class IdentifyUserQueryHandler : IRequestHandler<IdentifyUserQuery, IdentifyUserResult>
{
    private readonly IApplicationDbContext _context;

    public IdentifyUserQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IdentifyUserResult> Handle(IdentifyUserQuery request, CancellationToken cancellationToken)
    {
        var userId = await _context.Users
            .Where(u => u.Email == request.Email)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
            throw new NotFoundException("User", request.Email);

        return new IdentifyUserResult(userId.Value);
    }
}
