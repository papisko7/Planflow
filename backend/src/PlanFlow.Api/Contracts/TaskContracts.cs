using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Api.Contracts;

public record CreateTaskRequest(
    string Title,
    string? Description,
    Guid? AssignedUserId,
    DateTime? DeadlineUtc,
    int? ImpactScore,
    Guid? BlockedByTaskId,
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
    Guid? ItemListId = null);

// TaskType doubles as the marker for "this client knows the scheduling block": when it is null the stored type/start/effort/recurrence values are kept instead of being reset to defaults.
// EventTemplate is the same kind of marker for the template block (template, all-day, place, colour, busy/free):
// when it is null the stored values are kept. Reminders and ParticipantUserIds work the same way: null keeps what is
// stored (quick actions such as "done" send neither), a list - even an empty one - replaces it. ItemListId is the
// whole value: null takes the item out of its list, so the app resends the stored one with every update (a client that predates the
// scheduling block, TaskType null, keeps the stored list). Scope matters only for an item that repeats: which occurrences the
// edit applies to (0 this one, 1 this and the following, 2 all); the repeat rule fields are used only by the two last, because the rule
// belongs to the series.
public record UpdateTaskRequest(
    string Title,
    string? Description,
    Domain.Enums.TaskStatus Status,
    Guid? AssignedUserId,
    DateTime? DeadlineUtc,
    int? ImpactScore,
    Guid? BlockedByTaskId,
    double? ManualUrgencyOverride,
    DateTime? ManualUrgencyOverrideExpiresAtUtc,
    Domain.Enums.TaskType? TaskType = null,
    DateTime? StartUtc = null,
    double? EstimatedEffortHours = null,
    bool IsRecurring = false,
    Domain.Enums.RecurrenceFrequency? RecurrenceFrequency = null,
    int? RecurrenceInterval = null,
    int? RecurrenceWeekdays = null,
    DateTime? RecurrenceEndsAtUtc = null,
    DateTime? EndUtc = null,
    Domain.Enums.EventTemplate? EventTemplate = null,
    bool IsAllDay = false,
    string? Location = null,
    string? ColorKey = null,
    bool IsBusy = true,
    IReadOnlyList<ReminderInput>? Reminders = null,
    IReadOnlyList<Guid>? ParticipantUserIds = null,
    Guid? ItemListId = null,
    Domain.Enums.SeriesScope Scope = Domain.Enums.SeriesScope.This);

public record BulkTasksRequest(IReadOnlyList<Guid> TaskIds);

public record BulkStatusRequest(IReadOnlyList<Guid> TaskIds, Domain.Enums.TaskStatus Status);

/// <summary>ItemListId null takes the items out of whatever list they are in.</summary>
public record BulkMoveRequest(IReadOnlyList<Guid> TaskIds, Guid? ItemListId);

public record CreateItemListRequest(string Name, Domain.Enums.ItemListKind Kind);

public record RenameItemListRequest(string Name);
