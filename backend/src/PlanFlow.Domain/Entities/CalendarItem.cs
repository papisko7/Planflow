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
    public Enums.TaskStatus Status { get; set; } = Enums.TaskStatus.Backlog;

    /// <summary>Task = work to do (scored from deadline minus effort); Event = fixed-time happening (scored from <see cref="StartUtc"/>).</summary>
    public Enums.TaskType TaskType { get; set; } = Enums.TaskType.Task;

    /// <summary>Google-style template (Out of office, Focus time, ...). Always Default for a Task; decides the form fields and the scoring profile.</summary>
    public Enums.EventTemplate EventTemplate { get; set; } = Enums.EventTemplate.Default;

    /// <summary>Free-text place (events and Focus time) or the Working location label ("Home", "Office", custom).</summary>
    public string? Location { get; set; }

    /// <summary>Palette name (see ItemColors), not a hex value; null = default colour.</summary>
    public string? ColorKey { get; set; }

    /// <summary>Busy (blocks the person's time for availability) or Free. Meaningful only for events.</summary>
    public bool IsBusy { get; set; } = true;

    /// <summary>
    /// All-day item: the date is stored as midnight UTC of that calendar date and read literally (no zone conversion),
    /// so the day never shifts when the user travels; "end of day" is computed in the user's own time zone.
    /// <see cref="EndUtc"/> follows Google's convention: midnight UTC of the day AFTER the last day (exclusive), so a one-day
    /// event on 20 Oct is 20 Oct 00:00 to 21 Oct 00:00.
    /// </summary>
    public bool IsAllDay { get; set; }

    /// <summary>True when the deadline was filled in with "tomorrow" because the source (e.g. a Google task) had none.</summary>
    public bool IsDeadlineDefaulted { get; set; }

    /// <summary>Groups the occurrences of one repeating series (an imported event's recurringEventId, or an own series id).</summary>
    public string? SeriesId { get; set; }

    /// <summary>Last status seen in the external system; a sync changes our status only when this value changed, so the app's own edits are not undone.</summary>
    public string? ExternalStatus { get; set; }

    /// <summary>Hash of the raw external title + description, so the [planflow] tag is applied only when Google's text really changed.</summary>
    public string? ExternalDescriptionHash { get; set; }

    /// <summary>Read-only note about Google-only settings of an imported Out of office / Focus time event (auto-decline, do-not-disturb, message). Shown, never edited.</summary>
    public string? ExternalSettings { get; set; }

    /// <summary>Who assigned the item to <see cref="AssignedUserId"/>; null when nobody did (self-taken, imported, unassigned).</summary>
    public Guid? AssignedByUserId { get; set; }
    public User? AssignedByUser { get; set; }

    /// <summary>True when an Owner/Admin assigned the item: the assignee cannot give it back. Decided by the role at assignment time.</summary>
    public bool IsAssignmentLocked { get; set; }

    /// <summary>An Event: when it begins. A Task: its optional planned start (feeds the "Scheduled" part of the score; never later than the deadline).</summary>
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

    /// <summary>Google Tasks id this item was imported from (a Task, never an Event); null for everything else. With <see cref="SourceCalendarIntegrationId"/> it is the idempotency key SyncTasksJob upserts on.</summary>
    public string? ExternalTaskId { get; set; }

    /// <summary>The Google Tasks list the item belongs to; the app shows imported tasks grouped by it.</summary>
    public Guid? ExternalTaskListId { get; set; }
    public ExternalTaskList? ExternalTaskList { get; set; }

    /// <summary>
    /// True for an item imported from Google (Calendar or Tasks). Google owns its title, time, place, description, colour and template
    /// (a sync overwrites them), so the app lets the user change only the PlanFlow fields (impact, effort, status, assignee...).
    /// Derived, not stored.
    /// </summary>
    public bool IsExternal => ExternalCalendarEventId is not null || ExternalTaskId is not null;

    /// <summary>True for a task imported from Google Tasks. Its planned start belongs to PlanFlow (Google Tasks has no such field).</summary>
    public bool IsGoogleTask => ExternalTaskId is not null;

    public ICollection<CalendarItem> BlockedTasks { get; set; } = new List<CalendarItem>();
    public ICollection<TaskHistory> History { get; set; } = new List<TaskHistory>();
    public ICollection<UrgencyScoreLog> UrgencyScoreLogs { get; set; } = new List<UrgencyScoreLog>();
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
    public ICollection<ItemReminder> Reminders { get; set; } = new List<ItemReminder>();
}
