using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// A scheduled item owned by a team: either a Task (work with a deadline) or an Event (fixed time range).
/// One entity for both, so lists, sync and scoring share one pipeline. Named "CalendarItem" to avoid colliding
/// with System.Threading.Tasks.Task. Carries the current Urgency Score (computed by
/// UrgencyScoreCalculator in Phase 1) alongside the raw fields the algorithm reads.
/// </summary>
public class CalendarItem : BaseEntity
{
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public Guid? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Enums.TaskStatus Status { get; set; } = Enums.TaskStatus.Todo;

    /// <summary>Task = work to do (scored from deadline minus effort); Event = fixed-time happening (scored from <see cref="StartUtc"/>).</summary>
    public Enums.TaskType TaskType { get; set; } = Enums.TaskType.Task;

    /// <summary>Event only: when it begins. Always null for a Task.</summary>
    public DateTime? StartUtc { get; set; }

    /// <summary>Event only: when it ends. Null = unknown, the algorithm assumes a default duration. Always null for a Task.</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>Task only: the final due moment. Always null for an Event.</summary>
    public DateTime? DeadlineUtc { get; set; }

    /// <summary>The moment that matters to people: a Task's deadline, an Event's start. Derived, not stored.</summary>
    public DateTime? DueUtc => TaskType == Enums.TaskType.Event ? StartUtc : DeadlineUtc;

    /// <summary>Estimated working time in hours (the UI converts minutes/days/weeks/months). Null = unknown, the algorithm falls back to a per-type default.</summary>
    public double? EstimatedEffortHours { get; set; }

    /// <summary>Recurring item: a new instance is created every <see cref="RecurrenceIntervalHours"/> (scheduled by the recurring job).</summary>
    public bool IsRecurring { get; set; }
    public int? RecurrenceIntervalHours { get; set; }

    /// <summary>Optional end of the recurrence; no new instance is created after this moment.</summary>
    public DateTime? RecurrenceEndsAtUtc { get; set; }

    /// <summary>Opt-in per task: allow the Gemini layer to suggest an impact for it. Off by default; the core algorithm never depends on it.</summary>
    public bool UseAiEnhancement { get; set; }

    /// <summary>Impact (0–10) suggested by the AI layer. Never replaces <see cref="ImpactScore"/> — the user accepts or rejects it.</summary>
    public double? AiImpactScore { get; set; }
    public string? AiImpactReasoning { get; set; }

    /// <summary>True when the task was imported without enough data (e.g. no impact/effort in a calendar description) and the user should complete it.</summary>
    public bool IsDataIncomplete { get; set; }

    /// <summary>Impact on a 0–10 scale; null = not rated yet (e.g. imported from Google Calendar), which the algorithm treats as "unknown", not zero.</summary>
    public int? ImpactScore { get; set; }

    /// <summary>Task this one is blocked by, if any (used for the "blocking" urgency component).</summary>
    public Guid? BlockedByTaskId { get; set; }
    public CalendarItem? BlockedByTask { get; set; }

    /// <summary>Manual override in [0,1] applied to the "user override" urgency component; null = no override.</summary>
    public double? ManualUrgencyOverride { get; set; }
    public DateTime? ManualUrgencyOverrideExpiresAtUtc { get; set; }

    /// <summary>Latest computed urgency score in [0,1]; denormalized for fast dashboard sorting/queries.</summary>
    public double CurrentUrgencyScore { get; set; }

    /// <summary>Google Calendar event ID this task was synced from, if it originated from a calendar sync; null for manually-created tasks.</summary>
    public string? ExternalCalendarEventId { get; set; }

    /// <summary>The <see cref="CalendarIntegration"/> this task was synced from, if any. Combined with <see cref="ExternalCalendarEventId"/>, this is the idempotency key SyncCalendarJob upserts on.</summary>
    public Guid? SourceCalendarIntegrationId { get; set; }
    public CalendarIntegration? SourceCalendarIntegration { get; set; }

    public ICollection<CalendarItem> BlockedTasks { get; set; } = new List<CalendarItem>();
    public ICollection<TaskHistory> History { get; set; } = new List<TaskHistory>();
    public ICollection<UrgencyScoreLog> UrgencyScoreLogs { get; set; } = new List<UrgencyScoreLog>();
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
}
