using FluentValidation;

namespace PlanFlow.Application.Teams.Commands.ChangeMemberRole;

public class ChangeMemberRoleCommandValidator : AbstractValidator<ChangeMemberRoleCommand>
{
    public ChangeMemberRoleCommandValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.RequestingUserId).NotEmpty();
        RuleFor(x => x.MemberUserId).NotEmpty();
        RuleFor(x => x.NewRole).IsInEnum();
    }
}
