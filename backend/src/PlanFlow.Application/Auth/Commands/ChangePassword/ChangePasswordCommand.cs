using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Common.Security;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Auth.Commands.ChangePassword;

/// <summary>
/// Changes the password of a signed-in user. <paramref name="RefreshToken"/> is the calling device's own refresh token:
/// that device stays signed in, every other device is signed out.
/// </summary>
public record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword, string? RefreshToken) : IRequest;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEqual(x => x.CurrentPassword).WithMessage("The new password must differ from the current one.");
    }
}

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly INotificationService _notifications;

    public ChangePasswordCommandHandler(IApplicationDbContext context, IPasswordHasher passwordHasher, INotificationService notifications)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _notifications = notifications;
    }

    public async Task Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        // A wrong password is a 400, not a 401: the mobile client treats 401 as "token expired" and would sign the user out.
        if (!_passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
        {
            throw new ValidationException(new[] { new ValidationFailure("CurrentPassword", "Current password is incorrect.") });
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.UpdatedAtUtc = DateTime.UtcNow;

        // Sign out every other device (someone else may hold the old password) but keep the one the user is typing on.
        var keepHash = string.IsNullOrEmpty(request.RefreshToken) ? null : TokenHasher.Hash(request.RefreshToken);
        var others = await _context.RefreshSessions
            .Where(s => s.UserId == user.Id && s.TokenHash != keepHash)
            .ToListAsync(cancellationToken);
        _context.RefreshSessions.RemoveRange(others);

        _notifications.QueueDirect(NotificationEvent.PasswordChanged, user.Email, user.DisplayName,
            EmailTemplates.PasswordChanged(signedOutEverywhere: false));
        await _context.SaveChangesAsync(cancellationToken);
    }
}
