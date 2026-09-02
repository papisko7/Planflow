using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;
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

        if (user.RefreshTokenHash is null
            || user.RefreshTokenExpiresAtUtc is null
            || user.RefreshTokenExpiresAtUtc < DateTime.UtcNow
            || user.RefreshTokenHash != providedHash)
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

        user.RefreshTokenHash = TokenHasher.Hash(refreshToken);
        user.RefreshTokenExpiresAtUtc = refreshExpiresAtUtc;

        await _context.SaveChangesAsync(cancellationToken);

        return new AuthResultDto(user.Id, user.Email, user.DisplayName, accessToken, accessExpiresAtUtc, refreshToken);
    }
}
