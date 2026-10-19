using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Flat projection of a <see cref="CalendarItem"/> returned to callers outside the Application layer,
/// so handlers never leak EF Core-tracked entities across the CQRS boundary.
/// <see cref="IsExternal"/> marks an item imported from Google (its Google-owned fields are read-only in the app).
/// <see cref="IsDeadlineDefaulted"/> marks a Google task that had no due day (its deadline is a placeholder the user can still set).
/// <see cref="GoogleTaskListId"/> / <see cref="GoogleTaskListTitle"/> name the Google Tasks list an imported task belongs to (the title is filled by the queries that load it).
/// <see cref="ReasonText"/> is the latest urgency explanation; only the list query fills it (null elsewhere).
/// <see cref="ParticipantUserIds"/> are the guests of an event / co-contractors of a task (not the assignee); empty when the entity was loaded without them.
/// <see cref="IsAssignmentLocked"/> = an Owner/Admin assigned the item, so its assignee cannot give it back (<see cref="AssignedByUserId"/> is who did).
/// <see cref="SeriesId"/> groups the occurrences of one repeating item, from the app or from Google (null = not part of a series); the <c>Recurrence*</c> fields are the app's own repeat rule (null for an imported series, which Google owns); <see cref="ItemListId"/> is the team's own list the item sits in (null = none).
/// </summary>
public record TaskDto(
    Guid Id,
    Guid TeamId,
    Guid? AssignedUserId,
    string Title,
    string? Description,
    Domain.Enums.TaskStatus Status,
    DateTime? DeadlineUtc,
    int? ImpactScore,
    Guid? BlockedByTaskId,
    double? ManualUrgencyOverride,
    DateTime? ManualUrgencyOverrideExpiresAtUtc,
    double CurrentUrgencyScore,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Domain.Enums.TaskType TaskType,
    DateTime? StartUtc,
    DateTime? EndUtc,
    double? EstimatedEffortHours,
    bool IsRecurring,
    Domain.Enums.RecurrenceFrequency? RecurrenceFrequency,
    int? RecurrenceInterval,
    int? RecurrenceWeekdays,
    DateTime? RecurrenceEndsAtUtc,
    bool IsDataIncomplete,
    Domain.Enums.EventTemplate EventTemplate,
    string? Location,
    string? ColorKey,
    bool IsBusy,
    bool IsAllDay,
    string? ExternalSettings,
    bool IsExternal,
    bool IsDeadlineDefaulted = false,
    Guid? GoogleTaskListId = null,
    string? GoogleTaskListTitle = null,
    string? ReasonText = null,
    IReadOnlyList<Guid>? ParticipantUserIds = null,
    string? SeriesId = null,
    Guid? ItemListId = null,
    bool IsAssignmentLocked = false,
    Guid? AssignedByUserId = null)
{
    public static TaskDto FromEntity(CalendarItem task, RecurrenceSeries? series = null) => FromEntityCore(task, series ?? task.RecurrenceSeries);

    private static TaskDto FromEntityCore(CalendarItem task, RecurrenceSeries? series) => new(
        task.Id,
        task.TeamId,
        task.AssignedUserId,
        task.Title,
        task.Description,
        task.Status,
        task.DeadlineUtc,
        task.ImpactScore,
        task.BlockedByTaskId,
        task.ManualUrgencyOverride,
        task.ManualUrgencyOverrideExpiresAtUtc,
        task.CurrentUrgencyScore,
        task.CreatedAtUtc,
        task.UpdatedAtUtc,
        task.TaskType,
        task.StartUtc,
        task.EndUtc,
        task.EstimatedEffortHours,
        task.IsRecurring,
        series?.Frequency,
        series?.Interval,
        series is { Frequency: Domain.Enums.RecurrenceFrequency.Weekly } ? RecurrenceRules.EffectiveWeekdays(series, task.IsAllDay) : null,
        series?.EndsAtUtc,
        task.IsDataIncomplete,
        task.EventTemplate,
        task.Location,
        task.ColorKey,
        task.IsBusy,
        task.IsAllDay,
        task.ExternalSettings,
        task.IsExternal,
        task.IsDeadlineDefaulted,
        task.ExternalTaskListId,
        task.ExternalTaskList?.Title,
        ParticipantUserIds: task.Participants.OrderBy(p => p.CreatedAtUtc).Select(p => p.UserId).ToList(),
        SeriesId: task.SeriesKey,
        ItemListId: task.ItemListId,
        IsAssignmentLocked: task.IsAssignmentLocked,
        AssignedByUserId: task.AssignedByUserId);
}
