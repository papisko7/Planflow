using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Flat projection of a <see cref="TaskItem"/> returned to callers outside the Application layer,
/// so handlers never leak EF Core-tracked entities across the CQRS boundary.
/// </summary>
public record TaskDto(
    Guid Id,
    Guid TeamId,
    Guid? AssignedUserId,
    string Title,
    string? Description,
    Domain.Enums.TaskStatus Status,
    DateTime? DeadlineUtc,
    int ImpactScore,
    Guid? BlockedByTaskId,
    double? ManualUrgencyOverride,
    DateTime? ManualUrgencyOverrideExpiresAtUtc,
    double CurrentUrgencyScore,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc)
{
    public static TaskDto FromEntity(TaskItem task) => new(
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
        task.UpdatedAtUtc);
}
