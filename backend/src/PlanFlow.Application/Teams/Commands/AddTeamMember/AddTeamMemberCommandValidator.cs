using FluentValidation;

namespace PlanFlow.Application.Teams.Commands.AddTeamMember;

public class AddTeamMemberCommandValidator : AbstractValidator<AddTeamMemberCommand>
{
    public AddTeamMemberCommandValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.RequestingUserId).NotEmpty();
        RuleFor(x => x.NewMemberUserId).NotEmpty();
        RuleFor(x => x.Role).IsInEnum();
    }
}
