using FluentValidation;

namespace PlanFlow.Application.Tasks.Queries.GetTaskScoreHistory;

public class GetTaskScoreHistoryQueryValidator : AbstractValidator<GetTaskScoreHistoryQuery>
{
    public GetTaskScoreHistoryQueryValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
    }
}
