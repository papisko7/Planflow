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
    DateTime? EndUtc = null);

// TaskType doubles as the marker for "this client knows the scheduling block": when it is null (clients from before
// step 7.1) the stored type/start/effort/recurrence/AI values are kept instead of being reset to defaults.
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
    DateTime? EndUtc = null);
