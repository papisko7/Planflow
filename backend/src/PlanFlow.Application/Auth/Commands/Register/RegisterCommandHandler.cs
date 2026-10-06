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

namespace PlanFlow.Application.Auth.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly INotificationService _notifications;

    public RegisterCommandHandler(
        IApplicationDbContext context, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, INotificationService notifications)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _notifications = notifications;
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

        _context.Users.Add(user);
        _context.RefreshSessions.Add(new RefreshSession
        {
            UserId = user.Id,
            TokenHash = TokenHasher.Hash(refreshToken),
            ExpiresAtUtc = refreshExpiresAtUtc
        });
        await PersonalTeam.EnsureAsync(_context, user.Id, cancellationToken);

        // The registering device is the account's first known device, so signing in on it later is not "new".
        if (!string.IsNullOrWhiteSpace(request.DeviceId))
        {
            _context.UserDevices.Add(new UserDevice
            {
                UserId = user.Id,
                DeviceId = request.DeviceId.Trim(),
                Name = string.IsNullOrWhiteSpace(request.DeviceName) ? DeviceTracker.UnknownDeviceName : request.DeviceName.Trim()
            });
        }

        // One mail does both jobs: greets the new user and carries the code that confirms the address.
        var verificationCode = await OneTimeCodeIssuer.IssueAsync(_context, user.Id, OneTimeCodePurpose.EmailVerification, cancellationToken);
        _notifications.QueueDirect(NotificationEvent.Welcome, user.Email, user.DisplayName,
            EmailTemplates.Welcome(verificationCode!, OneTimeCodeIssuer.ValidMinutes));
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthResultDto(user.Id, user.Email, user.DisplayName, accessToken, accessExpiresAtUtc, refreshToken);
    }
}
