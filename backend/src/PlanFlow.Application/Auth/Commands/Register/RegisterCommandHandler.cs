using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Auth.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public RegisterCommandHandler(IApplicationDbContext context, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthResultDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var emailTaken = await _context.Users.AnyAsync(u => u.Email == request.Email, cancellationToken);
        if (emailTaken)
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = new User
        {
            Email = request.Email,
            DisplayName = request.DisplayName,
            PasswordHash = _passwordHasher.Hash(request.Password)
        };

        var (accessToken, accessExpiresAtUtc) = _jwtTokenService.GenerateAccessToken(user, teamRole: null, teamId: null);
        var (refreshToken, refreshExpiresAtUtc) = _jwtTokenService.GenerateRefreshToken();

        user.RefreshTokenHash = TokenHasher.Hash(refreshToken);
        user.RefreshTokenExpiresAtUtc = refreshExpiresAtUtc;

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthResultDto(user.Id, user.Email, user.DisplayName, accessToken, accessExpiresAtUtc, refreshToken);
    }
}
