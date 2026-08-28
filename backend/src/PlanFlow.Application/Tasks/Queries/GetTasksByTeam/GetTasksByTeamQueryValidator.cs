using FluentValidation;

namespace PlanFlow.Application.Tasks.Queries.GetTasksByTeam;

public class GetTasksByTeamQueryValidator : AbstractValidator<GetTasksByTeamQuery>
{
    public GetTasksByTeamQueryValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
    }
}
