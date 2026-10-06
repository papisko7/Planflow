using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Notifications.Inbox;

/// <summary>
/// The app tells the server "this phone can receive pushes at this address". Registering is idempotent (the app does it on
/// every sign-in), and a token already known under another account moves to the caller — the phone now belongs to them.
/// </summary>
public record RegisterPushTokenCommand(Guid UserId, string Token, string Platform) : IRequest;

public class RegisterPushTokenCommandValidator : AbstractValidator<RegisterPushTokenCommand>
{
    public RegisterPushTokenCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        // Expo tokens look like ExponentPushToken[xxxx]; anything else would only make the push service reject our batch.
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200)
            .Matches(@"^(Exponent|Expo)PushToken\[[^\]\s]+\]$").WithMessage("Not a valid Expo push token.");
        RuleFor(x => x.Platform).MaximumLength(20);
    }
}

public class RegisterPushTokenCommandHandler : IRequestHandler<RegisterPushTokenCommand>
{
    private readonly IApplicationDbContext _context;

    public RegisterPushTokenCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(RegisterPushTokenCommand request, CancellationToken cancellationToken)
    {
        var token = await _context.PushTokens.FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);
        if (token is null)
        {
            _context.PushTokens.Add(new PushToken { UserId = request.UserId, Token = request.Token, Platform = request.Platform });
        }
        else
        {
            token.UserId = request.UserId;
            token.Platform = request.Platform;
            token.LastSeenAtUtc = DateTime.UtcNow;
            token.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Forgets a phone's token (sign-out). Only the caller's own token is removed; an unknown token is not an error.</summary>
public record UnregisterPushTokenCommand(Guid UserId, string Token) : IRequest;

public class UnregisterPushTokenCommandHandler : IRequestHandler<UnregisterPushTokenCommand>
{
    private readonly IApplicationDbContext _context;

    public UnregisterPushTokenCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UnregisterPushTokenCommand request, CancellationToken cancellationToken)
    {
        var tokens = await _context.PushTokens
            .Where(t => t.UserId == request.UserId && t.Token == request.Token)
            .ToListAsync(cancellationToken);

        if (tokens.Count > 0)
        {
            _context.PushTokens.RemoveRange(tokens);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
