using MediatR;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Application.Tasks.Commands.UpdateTask;

/// <summary>
/// Overwrites a task's mutable fields and recalculates its Urgency Score. For an item that repeats, <c>Scope</c> says which occurrences
/// the change applies to (this one, this and the following ones, or all of them); an item that does not repeat ignores it.
/// </summary>
public record UpdateTaskCommand(
    Guid TaskId,
    string Title,
    string? Description,
    Domain.Enums.TaskStatus Status,
    Guid? AssignedUserId,
    DateTime? DeadlineUtc,
    int? ImpactScore,
    Guid? BlockedByTaskId,
    double? ManualUrgencyOverride,
    DateTime? ManualUrgencyOverrideExpiresAtUtc,
    Guid? UpdatedByUserId,
    Domain.Enums.TaskType TaskType = Domain.Enums.TaskType.Task,
    DateTime? StartUtc = null,
    double? EstimatedEffortHours = null,
    bool IsRecurring = false,
    Domain.Enums.RecurrenceFrequency? RecurrenceFrequency = null,
    int? RecurrenceInterval = null,
    int? RecurrenceWeekdays = null,
    DateTime? RecurrenceEndsAtUtc = null,
    DateTime? EndUtc = null,
    Domain.Enums.EventTemplate EventTemplate = Domain.Enums.EventTemplate.Default,
    bool IsAllDay = false,
    string? Location = null,
    string? ColorKey = null,
    bool IsBusy = true,
    IReadOnlyList<ReminderInput>? Reminders = null,
    IReadOnlyList<Guid>? ParticipantUserIds = null,
    Guid? ItemListId = null,
    bool CallerIsManager = true,
    Domain.Enums.SeriesScope Scope = Domain.Enums.SeriesScope.This) : IRequest<TaskDto>, ITaskScheduleFields;
