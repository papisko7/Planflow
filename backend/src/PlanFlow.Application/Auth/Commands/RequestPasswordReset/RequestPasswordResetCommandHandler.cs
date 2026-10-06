using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Auth.Commands.RequestPasswordReset;

public class RequestPasswordResetCommandHandler : IRequestHandler<RequestPasswordResetCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly INotificationService _notifications;

    public RequestPasswordResetCommandHandler(IApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task Handle(RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLower();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, cancellationToken);
        if (user is null)
        {
            return; // same silent outcome as for a real account
        }

        var code = await OneTimeCodeIssuer.IssueAsync(_context, user.Id, OneTimeCodePurpose.PasswordReset, cancellationToken);
        if (code is null)
        {
            return; // asked again within the cooldown
        }

        _notifications.QueueDirect(NotificationEvent.PasswordReset, user.Email, user.DisplayName,
            EmailTemplates.PasswordResetCode(code, OneTimeCodeIssuer.ValidMinutes));

        await _context.SaveChangesAsync(cancellationToken);
    }
}
