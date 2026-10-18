using FluentValidation;

namespace PlanFlow.Application.Users.Commands.UpdateTimeZone;

public class UpdateTimeZoneCommandValidator : AbstractValidator<UpdateTimeZoneCommand>
{
    public UpdateTimeZoneCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.TimeZoneId)
            .NotEmpty().MaximumLength(64)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
            .WithMessage("Unknown time zone. Use an IANA name such as 'Europe/Warsaw'.");
    }
}
