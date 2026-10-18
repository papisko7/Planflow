using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Flat projection of a <see cref="CalendarItem"/> returned to callers outside the Application layer,
/// so handlers never leak EF Core-tracked entities across the CQRS boundary.
/// <see cref="IsExternal"/> marks an item imported from Google (its Google-owned fields are read-only in the app).
/// <see cref="ReasonText"/> is the latest urgency explanation; only the list query fills it (null elsewhere).
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
    int? RecurrenceIntervalHours,
    DateTime? RecurrenceEndsAtUtc,
    bool UseAiEnhancement,
    double? AiImpactScore,
    string? AiImpactReasoning,
    bool IsDataIncomplete,
    Domain.Enums.EventTemplate EventTemplate,
    string? Location,
    string? ColorKey,
    bool IsBusy,
    bool IsAllDay,
    string? ExternalSettings,
    bool IsExternal,
    string? ReasonText = null)
{
    public static TaskDto FromEntity(CalendarItem task) => new(
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
        task.RecurrenceIntervalHours,
        task.RecurrenceEndsAtUtc,
        task.UseAiEnhancement,
        task.AiImpactScore,
        task.AiImpactReasoning,
        task.IsDataIncomplete,
        task.EventTemplate,
        task.Location,
        task.ColorKey,
        task.IsBusy,
        task.IsAllDay,
        task.ExternalSettings,
        task.IsExternal);
}
