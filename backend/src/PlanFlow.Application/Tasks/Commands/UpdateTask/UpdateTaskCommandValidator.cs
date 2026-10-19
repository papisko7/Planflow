using FluentValidation;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Commands.UpdateTask;

public class UpdateTaskCommandValidator : TaskScheduleValidator<UpdateTaskCommand>
{
    public UpdateTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ImpactScore).InclusiveBetween(0, 10).When(x => x.ImpactScore.HasValue);
        // Only known statuses pass (this also rejects the retired "Blocked" = 2). A status that is known but belongs to the
        // other item type is not an error: the handler maps it to the type's default (see TaskStatusRules.Normalize).
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.ManualUrgencyOverride)
            .InclusiveBetween(0.0, 1.0)
            .When(x => x.ManualUrgencyOverride.HasValue);
        RuleFor(x => x.Scope).IsInEnum();
    }
}
