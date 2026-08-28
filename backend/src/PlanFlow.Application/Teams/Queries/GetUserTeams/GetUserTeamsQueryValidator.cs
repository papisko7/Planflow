using FluentValidation;

namespace PlanFlow.Application.Teams.Queries.GetUserTeams;

public class GetUserTeamsQueryValidator : AbstractValidator<GetUserTeamsQuery>
{
    public GetUserTeamsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
