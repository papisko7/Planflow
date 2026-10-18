using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Snapshot of one <see cref="UrgencyScoreLog"/> entry, exposed so the mobile dashboard can
/// render the "why is this urgent" breakdown UI planned for Phase 3.4.
/// </summary>
public record UrgencyScoreBreakdownDto(
    double DeadlineComponent,
    double BlockingComponent,
    double? ImpactComponent,
    double UserOverrideComponent,
    double FinalScore,
    ScoreTriggerSource TriggerSource,
    DateTime CreatedAtUtc,
    bool IsBlocked,
    string? ReasonText = null)
{
    public static UrgencyScoreBreakdownDto FromEntity(UrgencyScoreLog log) => new(
        log.DeadlineComponent,
        log.BlockingComponent,
        log.ImpactComponent,
        log.UserOverrideComponent,
        log.FinalScore,
        log.TriggerSource,
        log.CreatedAtUtc,
        log.IsBlocked,
        log.ReasonText);
}

/// <summary>
/// One reminder of an item. Offset form: <see cref="OffsetMinutes"/> before the start. Day form: <see cref="DaysBefore"/> whole days
/// before, at <see cref="TimeOfDayMinutes"/> after midnight. The fields of the other form are null.
/// </summary>
public record ReminderDto(int? OffsetMinutes, int? DaysBefore, int? TimeOfDayMinutes)
{
    public static ReminderDto FromEntity(ItemReminder reminder) =>
        new(reminder.OffsetMinutes, reminder.DaysBefore, reminder.TimeOfDayMinutes);
}

/// <summary>Full detail view for a single task: the task itself, its latest urgency breakdown and its reminders.</summary>
public record TaskDetailDto(TaskDto Task, UrgencyScoreBreakdownDto? LatestScoreBreakdown, IReadOnlyList<ReminderDto>? Reminders = null);
