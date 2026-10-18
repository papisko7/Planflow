using FluentValidation;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>The scheduling and template fields shared by the create and update commands, so both are validated by the same rules.</summary>
public interface ITaskScheduleFields
{
    Domain.Enums.TaskType TaskType { get; }
    Domain.Enums.EventTemplate EventTemplate { get; }
    bool IsAllDay { get; }
    DateTime? StartUtc { get; }
    DateTime? EndUtc { get; }
    DateTime? DeadlineUtc { get; }
    double? EstimatedEffortHours { get; }
    bool IsRecurring { get; }
    int? RecurrenceIntervalHours { get; }
    DateTime? RecurrenceEndsAtUtc { get; }
    string? Description { get; }
    string? Location { get; }
    string? ColorKey { get; }
    bool IsBusy { get; }
}

public static class TaskScheduleMapper
{
    public const int MaxLocationLength = 512;

    /// <summary>
    /// Copies the scheduling and template fields onto the entity. What the type or template does not use is dropped, so stale values
    /// never linger: recurrence of a non-recurring item, the dates of the other type, and the place / colour / description of a
    /// template that has no such field (the form hides them, the server makes sure).
    /// </summary>
    public static void Apply(Domain.Entities.CalendarItem task, ITaskScheduleFields fields)
    {
        var isEvent = fields.TaskType == Domain.Enums.TaskType.Event;
        var rules = EventTemplateRules.For(fields.TaskType, fields.EventTemplate);
        var allDay = EventTemplateRules.ResolveAllDay(fields.TaskType, fields.EventTemplate, fields.IsAllDay);

        // An event turned into a task: its unchanged start is when the meeting began, not when work should begin, so it is not carried over.
        var staleEventStart = !isEvent && task.TaskType == Domain.Enums.TaskType.Event && fields.StartUtc == task.StartUtc;

        task.TaskType = fields.TaskType;
        task.EventTemplate = isEvent ? fields.EventTemplate : Domain.Enums.EventTemplate.Default;
        // A Task is due at a deadline; an Event occupies a time range. The fields of the other type are dropped,
        // so switching Event <-> Task never leaves stale dates behind.
        task.DeadlineUtc = isEvent ? null : fields.DeadlineUtc;
        // A Task may also carry a planned start (optional); it feeds the "Scheduled" part of its score.
        task.StartUtc = staleEventStart ? null : fields.StartUtc;
        // An all-day event without an end lasts that one day (end = the midnight after it, the exclusive end Google uses too).
        task.EndUtc = !isEvent ? null : allDay && fields.EndUtc is null && fields.StartUtc is { } start ? start.AddDays(1) : fields.EndUtc;
        task.IsAllDay = allDay;
        task.EstimatedEffortHours = fields.EstimatedEffortHours;
        task.IsRecurring = fields.IsRecurring;
        task.RecurrenceIntervalHours = fields.IsRecurring ? fields.RecurrenceIntervalHours : null;
        task.RecurrenceEndsAtUtc = fields.IsRecurring ? fields.RecurrenceEndsAtUtc : null;

        task.Description = rules.AllowsDescription && !string.IsNullOrWhiteSpace(fields.Description) ? fields.Description : null;
        task.Location = rules.Location == LocationMode.None || string.IsNullOrWhiteSpace(fields.Location) ? null : fields.Location.Trim();
        task.ColorKey = rules.AllowsColor ? fields.ColorKey : null;
        task.IsBusy = EventTemplateRules.ResolveBusy(fields.TaskType, fields.EventTemplate, fields.IsBusy);
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

        // A Task is always the Default template; an Event can be any known one.
        RuleFor(x => x.EventTemplate)
            .Must((x, template) => EventTemplateRules.IsValid(x.TaskType, template))
            .WithMessage("A task can only use the default template, and the template must be a known one.");
        RuleFor(x => x.ColorKey)
            .Must(ItemColors.IsValid).WithMessage("Unknown colour.");
        RuleFor(x => x.Location)
            .MaximumLength(TaskScheduleMapper.MaxLocationLength);

        // An all-day event is stored as whole days: midnight UTC of the first day, and (exclusive, as in Google) midnight UTC of
        // the day after the last one. The form builds exactly that; anything else would show on the wrong day for other time zones.
        When(IsAllDayEvent, () =>
        {
            RuleFor(x => x.StartUtc)
                .Must(start => start is null || start.Value.TimeOfDay == TimeSpan.Zero)
                .WithMessage("An all-day event must start at midnight UTC (whole days).");
            RuleFor(x => x.EndUtc)
                .Must(end => end is null || end.Value.TimeOfDay == TimeSpan.Zero)
                .WithMessage("An all-day event must end at midnight UTC (whole days).");
            RuleFor(x => x)
                .Must(x => x.StartUtc!.Value.AddDays(1) <= x.EndUtc!.Value)
                .When(x => x.StartUtc.HasValue && x.EndUtc.HasValue)
                .OverridePropertyName("EndUtc")
                .WithMessage("An all-day event lasts at least one day (the end is the midnight after its last day).");
        });

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

        // A task cannot be planned to start after it is due.
        RuleFor(x => x)
            .Must(x => x.StartUtc!.Value <= x.DeadlineUtc!.Value)
            .When(x => x.TaskType == Domain.Enums.TaskType.Task && x.StartUtc.HasValue && x.DeadlineUtc.HasValue)
            .OverridePropertyName("StartUtc")
            .WithMessage("A task cannot start after its deadline.");

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

    /// <summary>True for an Event that ends up all-day after the template's rule (a Birthday always, Focus time never).</summary>
    protected static bool IsAllDayEvent(T x) =>
        x.TaskType == Domain.Enums.TaskType.Event
        && EventTemplateRules.IsValid(x.TaskType, x.EventTemplate)
        && EventTemplateRules.ResolveAllDay(x.TaskType, x.EventTemplate, x.IsAllDay);

    /// <summary>The first occurrence a recurring item repeats from: an Event's start, a Task's deadline.</summary>
    private static DateTime? RecurrenceAnchor(T x) =>
        x.TaskType == Domain.Enums.TaskType.Event ? x.StartUtc : x.DeadlineUtc;
}
