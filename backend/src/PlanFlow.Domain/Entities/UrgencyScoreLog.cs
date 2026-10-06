using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// Snapshot of one Urgency Score computation for a <see cref="TaskItem"/>, capturing every
/// weighted component so the result is explainable and reproducible for the thesis evaluation:
/// weighted sum 0.45*Deadline + 0.20*Blocking + 0.25*Impact + 0.10*UserOverride, then the deadline and
/// inheritance floors and, for blocked tasks, the blocked penalty (see UrgencyScoreCalculator).
/// </summary>
public class UrgencyScoreLog : BaseEntity
{
    public Guid TaskItemId { get; set; }
    public TaskItem TaskItem { get; set; } = null!;

    public double DeadlineComponent { get; set; }
    public double BlockingComponent { get; set; }

    /// <summary>Null when the task's impact was not rated at the time of this calculation.</summary>
    public double? ImpactComponent { get; set; }
    public double UserOverrideComponent { get; set; }

    /// <summary>True when the task was waiting on an unfinished blocker, so its score was reduced.</summary>
    public bool IsBlocked { get; set; }

    public double FinalScore { get; set; }

    /// <summary>Describes the origin of this score calculation (manual edit, scheduled job, calendar sync, etc.).</summary>
    public ScoreTriggerSource TriggerSource { get; set; }
}
