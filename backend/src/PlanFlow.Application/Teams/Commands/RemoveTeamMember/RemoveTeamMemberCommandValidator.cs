using FluentValidation;

namespace PlanFlow.Application.Teams.Commands.RemoveTeamMember;

public class RemoveTeamMemberCommandValidator : AbstractValidator<RemoveTeamMemberCommand>
{
    public RemoveTeamMemberCommandValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.RequestingUserId).NotEmpty();
        RuleFor(x => x.MemberUserId).NotEmpty();
    }
}
