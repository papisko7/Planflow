using FluentValidation;

namespace PlanFlow.Application.Alerts.Commands.CreateAlert;

public class CreateAlertCommandValidator : AbstractValidator<CreateAlertCommand>
{
    public CreateAlertCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(512);
    }
}
