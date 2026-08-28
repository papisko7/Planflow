using FluentValidation;

namespace PlanFlow.Application.Teams.Commands.CreateTeam;

public class CreateTeamCommandValidator : AbstractValidator<CreateTeamCommand>
{
    public CreateTeamCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OwnerUserId).NotEmpty();
    }
}
