using FluentValidation;

namespace PlanFlow.Application.Teams.Commands.DeleteTeam;

public class DeleteTeamCommandValidator : AbstractValidator<DeleteTeamCommand>
{
    public DeleteTeamCommandValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.RequestingUserId).NotEmpty();
    }
}
