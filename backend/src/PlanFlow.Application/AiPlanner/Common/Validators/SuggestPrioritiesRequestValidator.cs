using FluentValidation;
using PlanFlow.Application.AiPlanner.Common.Dtos;

namespace PlanFlow.Application.AiPlanner.Common.Validators;

public class SuggestPrioritiesRequestValidator : AbstractValidator<SuggestPrioritiesRequest>
{
    public SuggestPrioritiesRequestValidator()
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
