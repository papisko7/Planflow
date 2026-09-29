using FluentValidation;

namespace PlanFlow.Application.Teams.Queries.GetTeamMembers;

public class GetTeamMembersQueryValidator : AbstractValidator<GetTeamMembersQuery>
{
    public GetTeamMembersQueryValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.RequestingUserId).NotEmpty();
    }
}
