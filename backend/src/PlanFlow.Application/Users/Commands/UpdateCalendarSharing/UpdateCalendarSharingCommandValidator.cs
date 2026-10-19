using FluentValidation;

namespace PlanFlow.Application.Users.Commands.UpdateCalendarSharing;

public class UpdateCalendarSharingCommandValidator : AbstractValidator<UpdateCalendarSharingCommand>
{
    public UpdateCalendarSharingCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Level).IsInEnum();
    }
}
