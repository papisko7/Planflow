using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Auth.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Auth.Commands.VerifyEmail;

/// <summary>Confirms the caller's e-mail address with the 6-digit code that was mailed to it.</summary>
public record VerifyEmailCommand(Guid UserId, string Code) : IRequest;

public class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Matches(@"^\d{6}$").WithMessage("The code is 6 digits.");
    }
}

public class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand>
{
    private readonly IApplicationDbContext _context;

    public VerifyEmailCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        if (user.IsEmailVerified)
        {
            return; // already confirmed: nothing to do, and a repeated tap must not fail
        }

        // 400 (not 401): the mobile client treats 401 as an expired session and would try to refresh it.
        _ = await OneTimeCodeIssuer.TryConsumeAsync(_context, user.Id, OneTimeCodePurpose.EmailVerification, request.Code, cancellationToken)
            ?? throw new ValidationException(new[] { new ValidationFailure("Code", "The code is invalid or has expired. Request a new one.") });

        user.EmailVerifiedAtUtc = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
