using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Teams.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly INotificationService _notifications;

    public LoginCommandHandler(
        IApplicationDbContext context, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, INotificationService notifications)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _notifications = notifications;
    }

    public async Task<AuthResultDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken)
            ?? throw new UnauthorizedException("Invalid email or password.");

        if (!_passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            throw new UnauthorizedException("Invalid email or password.");
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

        // One session per device: a new login adds its own refresh token and leaves the other
        // devices signed in. Expired sessions are dropped here so the table does not grow forever.
        var expired = await _context.RefreshSessions
            .Where(s => s.UserId == user.Id && s.ExpiresAtUtc < DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        _context.RefreshSessions.RemoveRange(expired);
        _context.RefreshSessions.Add(new RefreshSession
        {
            UserId = user.Id,
            TokenHash = TokenHasher.Hash(refreshToken),
            ExpiresAtUtc = refreshExpiresAtUtc
        });
        user.LastLoginAtUtc = DateTime.UtcNow;
        await PersonalTeam.EnsureAsync(_context, user.Id, cancellationToken);

        // Sign-in alert: by default only for a device the account has not used before (see NotificationPolicy.ShouldAlertLogin).
        var isNewDevice = await DeviceTracker.RecordSignInAsync(_context, user.Id, request.DeviceId, request.DeviceName, cancellationToken);
        var preferences = await _context.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken)
            ?? NotificationPreference.CreateDefault(user.Id);
        if (user.IsEmailVerified && NotificationPolicy.ShouldAlertLogin(preferences, isNewDevice))
        {
            var deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? DeviceTracker.UnknownDeviceName : request.DeviceName.Trim();
            _notifications.QueueDirect(NotificationEvent.NewDeviceLogin, user.Email, user.DisplayName,
                EmailTemplates.NewDeviceLogin(deviceName, DateTime.UtcNow));
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new AuthResultDto(user.Id, user.Email, user.DisplayName, accessToken, accessExpiresAtUtc, refreshToken);
    }
}
