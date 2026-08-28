using FluentValidation;

namespace PlanFlow.Application.Tasks.Commands.UpdateTask;

public class UpdateTaskCommandValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ImpactScore).InclusiveBetween(0, 10);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.ManualUrgencyOverride)
            .InclusiveBetween(0.0, 1.0)
            .When(x => x.ManualUrgencyOverride.HasValue);
    }
}
