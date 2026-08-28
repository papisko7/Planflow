using FluentValidation;

namespace PlanFlow.Application.Tasks.Queries.GetTaskDetail;

public class GetTaskDetailQueryValidator : AbstractValidator<GetTaskDetailQuery>
{
    public GetTaskDetailQueryValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
    }
}
