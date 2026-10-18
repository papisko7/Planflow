using FluentValidation;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Commands.CreateTask;

public class CreateTaskCommandValidator : TaskScheduleValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ImpactScore).InclusiveBetween(0, 10).When(x => x.ImpactScore.HasValue);
        RuleFor(x => x.DeadlineUtc)
            .GreaterThan(DateTime.UtcNow)
            .When(x => x.TaskType == Domain.Enums.TaskType.Task && x.DeadlineUtc.HasValue)
            .WithMessage("Deadline must be in the future.");

        // A new event that is already over is pointless: it would be scored 0 straight away. (Editing a past one is allowed.)
        RuleFor(x => x.EndUtc)
            .Must((x, _) => EventEndsAfterNow(x))
            .When(x => x.TaskType == Domain.Enums.TaskType.Event && x.StartUtc.HasValue)
            .WithMessage("The event must not be already over.");
    }

    /// <summary>
    /// An all-day event ends at a midnight UTC but means the last day in the user's own zone, which can still be running for hours after
    /// that moment (up to 12 h west of UTC), so it counts as over only once that margin has passed too.
    /// </summary>
    private static bool EventEndsAfterNow(CreateTaskCommand x)
    {
        if (IsAllDayEvent(x))
        {
            var dayEnd = x.EndUtc ?? x.StartUtc!.Value.AddDays(1);
            return dayEnd.AddHours(12) > DateTime.UtcNow;
        }

        return CalendarItemUrgencyScoreFactory.ResolveEventEnd(x.StartUtc, x.EndUtc) > DateTime.UtcNow;
    }
}
