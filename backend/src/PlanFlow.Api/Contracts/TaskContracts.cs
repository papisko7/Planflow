using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Api.Contracts;

// The trailing scheduling/AI fields are optional so existing clients that don't send them keep working
// (they get a plain Task with no effort estimate, no recurrence and AI off).
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
    int? RecurrenceIntervalHours = null,
    DateTime? RecurrenceEndsAtUtc = null,
    bool UseAiEnhancement = false,
    DateTime? EndUtc = null,
    Domain.Enums.EventTemplate EventTemplate = Domain.Enums.EventTemplate.Default,
    bool IsAllDay = false,
    string? Location = null,
    string? ColorKey = null,
    bool IsBusy = true,
    IReadOnlyList<ReminderInput>? Reminders = null,
    IReadOnlyList<Guid>? ParticipantUserIds = null,
    Guid? ItemListId = null);

// TaskType doubles as the marker for "this client knows the scheduling block": when it is null (clients from before
// step 7.1) the stored type/start/effort/recurrence/AI values are kept instead of being reset to defaults.
// EventTemplate is the same kind of marker for the template block (template, all-day, place, colour, busy/free; step 7.5.5):
// when it is null the stored values are kept. Reminders and ParticipantUserIds work the same way (step 7.5.6): null keeps what is
// stored (quick actions such as "done" send neither), a list - even an empty one - replaces it. ItemListId (step 7.5.11) is the
// whole value: null takes the item out of its list, so the app resends the stored one with every update (a client that predates the
// scheduling block, TaskType null, keeps the stored list).
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
    int? RecurrenceIntervalHours = null,
    DateTime? RecurrenceEndsAtUtc = null,
    bool UseAiEnhancement = false,
    DateTime? EndUtc = null,
    Domain.Enums.EventTemplate? EventTemplate = null,
    bool IsAllDay = false,
    string? Location = null,
    string? ColorKey = null,
    bool IsBusy = true,
    IReadOnlyList<ReminderInput>? Reminders = null,
    IReadOnlyList<Guid>? ParticipantUserIds = null,
    Guid? ItemListId = null);

// Bulk actions (step 7.5.11): selection mode and "Clear". The ids belong to the team in the URL; the reply says what was left alone.
public record BulkTasksRequest(IReadOnlyList<Guid> TaskIds);

public record BulkStatusRequest(IReadOnlyList<Guid> TaskIds, Domain.Enums.TaskStatus Status);

/// <summary>ItemListId null takes the items out of whatever list they are in.</summary>
public record BulkMoveRequest(IReadOnlyList<Guid> TaskIds, Guid? ItemListId);

// The team's own lists (step 7.5.11).
public record CreateItemListRequest(string Name, Domain.Enums.ItemListKind Kind);

public record RenameItemListRequest(string Name);
