using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// A unit of work owned by a team. Named "TaskItem" rather than "Task" to avoid colliding
/// with System.Threading.Tasks.Task. Carries the current Urgency Score (computed by
/// UrgencyScoreCalculator in Phase 1) alongside the raw fields the algorithm reads.
/// </summary>
public class TaskItem : BaseEntity
{
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public Guid? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Enums.TaskStatus Status { get; set; } = Enums.TaskStatus.Todo;

    public DateTime? DeadlineUtc { get; set; }
    public int ImpactScore { get; set; }

    /// <summary>Task this one is blocked by, if any (used for the "blocking" urgency component).</summary>
    public Guid? BlockedByTaskId { get; set; }
    public TaskItem? BlockedByTask { get; set; }

    /// <summary>Manual override in [0,1] applied to the "user override" urgency component; null = no override.</summary>
    public double? ManualUrgencyOverride { get; set; }
    public DateTime? ManualUrgencyOverrideExpiresAtUtc { get; set; }

    /// <summary>Latest computed urgency score in [0,1]; denormalized for fast dashboard sorting/queries.</summary>
    public double CurrentUrgencyScore { get; set; }

    /// <summary>AI-assessed importance from the AI Planner (Phase 2.3); normalized to [0,1]. Null = not yet assessed or assessment failed (fallback to 0).</summary>
    public double? AiAssessmentScore { get; set; }

    /// <summary>Timestamp of the last successful AI assessment, used to avoid redundant API calls.</summary>
    public DateTime? AiAssessmentAtUtc { get; set; }

    public ICollection<TaskItem> BlockedTasks { get; set; } = new List<TaskItem>();
    public ICollection<TaskHistory> History { get; set; } = new List<TaskHistory>();
    public ICollection<UrgencyScoreLog> UrgencyScoreLogs { get; set; } = new List<UrgencyScoreLog>();
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
}
