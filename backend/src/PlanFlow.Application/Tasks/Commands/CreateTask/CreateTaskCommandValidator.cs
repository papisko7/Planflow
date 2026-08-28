using FluentValidation;

namespace PlanFlow.Application.Tasks.Commands.CreateTask;

public class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ImpactScore).InclusiveBetween(0, 10);
        RuleFor(x => x.DeadlineUtc)
            .GreaterThan(DateTime.UtcNow)
            .When(x => x.DeadlineUtc.HasValue)
            .WithMessage("Deadline must be in the future.");
    }
}
