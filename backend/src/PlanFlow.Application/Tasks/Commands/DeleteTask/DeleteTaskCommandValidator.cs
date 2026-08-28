using FluentValidation;

namespace PlanFlow.Application.Tasks.Commands.DeleteTask;

public class DeleteTaskCommandValidator : AbstractValidator<DeleteTaskCommand>
{
    public DeleteTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
    }
}
