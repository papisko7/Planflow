using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Auth.Commands.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly INotificationService _notifications;

    public ResetPasswordCommandHandler(IApplicationDbContext context, IPasswordHasher passwordHasher, INotificationService notifications)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _notifications = notifications;
    }

    public async Task Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLower();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, cancellationToken)
            ?? throw InvalidCode();

        // A wrong guess is counted inside TryConsumeAsync: five wrong guesses lock the code for good.
        _ = await OneTimeCodeIssuer.TryConsumeAsync(_context, user.Id, OneTimeCodePurpose.PasswordReset, request.Code, cancellationToken)
            ?? throw InvalidCode();

        var nowUtc = DateTime.UtcNow;
        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.UpdatedAtUtc = nowUtc;

        // Receiving the reset code at this address proves the owner reads it, so the address counts as confirmed too.
        user.EmailVerifiedAtUtc ??= nowUtc;

        // A reset usually means the old password may be known to someone else: sign out every device.
        var sessions = await _context.RefreshSessions.Where(s => s.UserId == user.Id).ToListAsync(cancellationToken);
        _context.RefreshSessions.RemoveRange(sessions);

        _notifications.QueueDirect(NotificationEvent.PasswordChanged, user.Email, user.DisplayName, EmailTemplates.PasswordChanged());
        await _context.SaveChangesAsync(cancellationToken);
    }

    // One message for "no such account", "wrong code", "expired" and "locked": the caller learns nothing extra.
    // It is a 400 (not a 401) because the mobile client treats 401 as an expired session and would try to refresh it.
    private static ValidationException InvalidCode() =>
        new(new[] { new ValidationFailure("Code", "The code is invalid or has expired. Request a new one.") });
}
