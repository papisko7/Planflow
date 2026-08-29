using FluentValidation;
using PlanFlow.Application.AiPlanner.Common.Dtos;

namespace PlanFlow.Application.AiPlanner.Common.Validators;

public class GeneratePlanRequestValidator : AbstractValidator<GeneratePlanRequest>
{
    public GeneratePlanRequestValidator()
    {
        RuleFor(x => x.Tasks)
            .NotEmpty().WithMessage("Tasks list cannot be empty")
            .Must(x => x.Count <= 100).WithMessage("Tasks list cannot exceed 100 items");

        RuleForEach(x => x.Tasks)
            .SetValidator(new PlanTaskContextValidator());

        RuleFor(x => x.UserContext)
            .NotEmpty().WithMessage("User context is required")
            .MaximumLength(2000).WithMessage("User context cannot exceed 2000 characters");
    }
}

public class PlanTaskContextValidator : AbstractValidator<PlanTaskContext>
{
    public PlanTaskContextValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty().WithMessage("Task ID is required");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Task title is required")
            .MaximumLength(200).WithMessage("Task title cannot exceed 200 characters");

        RuleFor(x => x.BlockedTaskCount)
            .GreaterThanOrEqualTo(0).WithMessage("Blocked task count cannot be negative");

        RuleFor(x => x.ImpactScore)
            .InclusiveBetween(0, 10).WithMessage("Impact score must be between 0 and 10");

        RuleFor(x => x.UrgencyScore)
            .InclusiveBetween(0.0, 1.0).WithMessage("Urgency score must be between 0.0 and 1.0");
    }
}
