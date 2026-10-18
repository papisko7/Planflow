using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Users.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Users.Commands.UpdateProfile;

public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, UserProfileDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly INotificationService _notifications;

    public UpdateProfileCommandHandler(IApplicationDbContext context, IPasswordHasher passwordHasher, INotificationService notifications)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _notifications = notifications;
    }

    public async Task<UserProfileDto> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        var newEmail = request.Email.Trim();
        var emailChanged = !string.Equals(newEmail, user.Email, StringComparison.Ordinal);

        if (emailChanged)
        {
            // A wrong password is reported as a 400 validation error, not a 401: the mobile client treats 401
            // as "token expired" and would otherwise try to refresh and possibly sign the user out.
            if (string.IsNullOrEmpty(request.CurrentPassword) || !_passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
            {
                throw new ValidationException(new[] { new ValidationFailure("CurrentPassword", "Current password is incorrect.") });
            }

            var lowered = newEmail.ToLower();
            var taken = await _context.Users.AnyAsync(u => u.Id != user.Id && u.Email.ToLower() == lowered, cancellationToken);
            if (taken)
            {
                throw new ConflictException("An account with this email already exists.");
            }
        }

        user.DisplayName = request.DisplayName.Trim();
        user.Email = newEmail;
        user.UpdatedAtUtc = DateTime.UtcNow;

        if (emailChanged)
        {
            // The new address is unproven until its owner types the code we mail there.
            user.EmailVerifiedAtUtc = null;
            var code = await OneTimeCodeIssuer.IssueAsync(
                _context, user.Id, OneTimeCodePurpose.EmailVerification, cancellationToken, ignoreCooldown: true);
            _notifications.QueueDirect(NotificationEvent.EmailVerification, user.Email, user.DisplayName,
                EmailTemplates.VerifyEmailCode(code!, OneTimeCodeIssuer.ValidMinutes));
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new UserProfileDto(user.Id, user.Email, user.DisplayName, user.IsEmailVerified, user.TimeZoneId);
    }
}
