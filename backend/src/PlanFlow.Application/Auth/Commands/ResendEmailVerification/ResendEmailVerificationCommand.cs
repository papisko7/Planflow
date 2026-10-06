using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Auth.Commands.ResendEmailVerification;

/// <summary>Mails a fresh confirmation code to the caller's address. Silently does nothing if it is already confirmed or a code was sent a moment ago.</summary>
public record ResendEmailVerificationCommand(Guid UserId) : IRequest;

public class ResendEmailVerificationCommandHandler : IRequestHandler<ResendEmailVerificationCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly INotificationService _notifications;

    public ResendEmailVerificationCommandHandler(IApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task Handle(ResendEmailVerificationCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        if (user.IsEmailVerified)
        {
            return;
        }

        var code = await OneTimeCodeIssuer.IssueAsync(_context, user.Id, OneTimeCodePurpose.EmailVerification, cancellationToken);
        if (code is null)
        {
            return; // asked again within the cooldown
        }

        _notifications.QueueDirect(NotificationEvent.EmailVerification, user.Email, user.DisplayName,
            EmailTemplates.VerifyEmailCode(code, OneTimeCodeIssuer.ValidMinutes));
        await _context.SaveChangesAsync(cancellationToken);
    }
}
