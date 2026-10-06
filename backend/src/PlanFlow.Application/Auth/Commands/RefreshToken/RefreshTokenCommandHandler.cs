using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;
using PlanFlow.Application.Teams.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Auth.Commands.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;

    public RefreshTokenCommandHandler(IApplicationDbContext context, IJwtTokenService jwtTokenService)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthResultDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new UnauthorizedException("Invalid refresh token.");

        var providedHash = TokenHasher.Hash(request.RefreshToken);

        // Look the token up among this user's device sessions; each refresh replaces only that session's token.
        var session = await _context.RefreshSessions
            .FirstOrDefaultAsync(s => s.UserId == user.Id && s.TokenHash == providedHash, cancellationToken);

        if (session is null || session.ExpiresAtUtc < DateTime.UtcNow)
        {
            throw new UnauthorizedException("Invalid refresh token.");
        }

        TeamRole? teamRole = null;
        if (request.TeamId is not null)
        {
            var membership = await _context.TeamMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.TeamId == request.TeamId && m.UserId == user.Id, cancellationToken)
                ?? throw new ForbiddenAccessException("You are not a member of this team.");

            teamRole = membership.Role;
        }

        var (accessToken, accessExpiresAtUtc) = _jwtTokenService.GenerateAccessToken(user, teamRole, request.TeamId);
        var (refreshToken, refreshExpiresAtUtc) = _jwtTokenService.GenerateRefreshToken();

        session.TokenHash = TokenHasher.Hash(refreshToken);
        session.ExpiresAtUtc = refreshExpiresAtUtc;
        session.UpdatedAtUtc = DateTime.UtcNow;
        await PersonalTeam.EnsureAsync(_context, user.Id, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return new AuthResultDto(user.Id, user.Email, user.DisplayName, accessToken, accessExpiresAtUtc, refreshToken);
    }
}
