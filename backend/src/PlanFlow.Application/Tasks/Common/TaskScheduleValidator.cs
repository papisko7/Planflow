using FluentValidation;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>The scheduling fields shared by the create and update commands, so both are validated by the same rules.</summary>
public interface ITaskScheduleFields
{
    Domain.Enums.TaskType TaskType { get; }
    DateTime? StartUtc { get; }
    DateTime? EndUtc { get; }
    DateTime? DeadlineUtc { get; }
    double? EstimatedEffortHours { get; }
    bool IsRecurring { get; }
    int? RecurrenceIntervalHours { get; }
    DateTime? RecurrenceEndsAtUtc { get; }
}

public static class TaskScheduleMapper
{
    /// <summary>Copies the scheduling fields onto the entity. Recurrence settings of a non-recurring item are dropped, so stale values never linger.</summary>
    public static void Apply(Domain.Entities.CalendarItem task, ITaskScheduleFields fields)
    {
        var isEvent = fields.TaskType == Domain.Enums.TaskType.Event;
        task.TaskType = fields.TaskType;
        // A Task is due at a deadline; an Event occupies a time range. The fields of the other type are dropped,
        // so switching Event <-> Task never leaves stale dates behind.
        task.DeadlineUtc = isEvent ? null : fields.DeadlineUtc;
        task.StartUtc = isEvent ? fields.StartUtc : null;
        task.EndUtc = isEvent ? fields.EndUtc : null;
        task.EstimatedEffortHours = fields.EstimatedEffortHours;
        task.IsRecurring = fields.IsRecurring;
        task.RecurrenceIntervalHours = fields.IsRecurring ? fields.RecurrenceIntervalHours : null;
        task.RecurrenceEndsAtUtc = fields.IsRecurring ? fields.RecurrenceEndsAtUtc : null;
    }
}

/// <summary>Rules for type, dates, effort and recurrence that must hold for every task regardless of who creates or edits it.</summary>
public abstract class TaskScheduleValidator<T> : AbstractValidator<T> where T : ITaskScheduleFields
{
    /// <summary>Upper bound for one effort estimate / recurrence interval: one year in hours.</summary>
    private const int MaxHours = 366 * 24;

    protected TaskScheduleValidator()
    {
        RuleFor(x => x.TaskType).IsInEnum();

        RuleFor(x => x.EstimatedEffortHours)
            .GreaterThan(0).LessThanOrEqualTo(MaxHours)
            .When(x => x.EstimatedEffortHours.HasValue)
            .WithMessage($"Estimated effort must be greater than 0 and at most {MaxHours} hours.");

        RuleFor(x => x.StartUtc)
            .NotNull()
            .When(x => x.TaskType == Domain.Enums.TaskType.Event)
            .WithMessage("An event needs a start time.");

        // The end of an event may be left out (a default duration is assumed), but if given it cannot precede the start.
        RuleFor(x => x)
            .Must(x => x.StartUtc!.Value <= x.EndUtc!.Value)
            .When(x => x.TaskType == Domain.Enums.TaskType.Event && x.StartUtc.HasValue && x.EndUtc.HasValue)
            .OverridePropertyName("EndUtc")
            .WithMessage("End must not be earlier than the start.");

        When(x => x.IsRecurring, () =>
        {
            RuleFor(x => x.RecurrenceIntervalHours)
                .NotNull().WithMessage("A recurring item needs an interval.")
                .InclusiveBetween(1, MaxHours).WithMessage($"Interval must be between 1 and {MaxHours} hours.");

            RuleFor(x => x)
                .Must(x => RecurrenceAnchor(x).HasValue)
                .OverridePropertyName("DeadlineUtc")
                .WithMessage("A recurring item needs a start (event) or a deadline (task) to repeat from.");

            RuleFor(x => x)
                .Must(x => x.RecurrenceEndsAtUtc!.Value > (RecurrenceAnchor(x) ?? DateTime.MinValue))
                .When(x => x.RecurrenceEndsAtUtc.HasValue)
                .OverridePropertyName("RecurrenceEndsAtUtc")
                .WithMessage("The end of the recurrence must be after the first occurrence.");
        });
    }

    /// <summary>The first occurrence a recurring item repeats from: an Event's start, a Task's deadline.</summary>
    private static DateTime? RecurrenceAnchor(T x) =>
        x.TaskType == Domain.Enums.TaskType.Event ? x.StartUtc : x.DeadlineUtc;
}
