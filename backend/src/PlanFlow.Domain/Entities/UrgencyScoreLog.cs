using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// Snapshot of one Urgency Score computation for a <see cref="CalendarItem"/>, capturing every
/// weighted component so the result is explainable and reproducible for the thesis evaluation:
/// weighted sum 0.45*Deadline + 0.20*Blocking + 0.25*Impact + 0.10*UserOverride, then the deadline and
/// inheritance floors and, for blocked tasks, the blocked penalty (see UrgencyScoreCalculator).
/// </summary>
public class UrgencyScoreLog : BaseEntity
{
    public Guid CalendarItemId { get; set; }
    public CalendarItem CalendarItem { get; set; } = null!;

    public double DeadlineComponent { get; set; }
    public double BlockingComponent { get; set; }

    /// <summary>Null when the task's impact was not rated at the time of this calculation.</summary>
    public double? ImpactComponent { get; set; }
    public double UserOverrideComponent { get; set; }

    /// <summary>The "time to the task's Start" component (algorithm v2); null on rows written by v1, which had no such component.</summary>
    public double? ScheduledComponent { get; set; }

    /// <summary>Which version of the weights/rules produced this row, so history before and after a weight change is never compared blindly. 1 = original weights.</summary>
    public int AlgorithmVersion { get; set; } = 1;

    /// <summary>True when the task was waiting on an unfinished blocker, so its score was reduced.</summary>
    public bool IsBlocked { get; set; }

    public double FinalScore { get; set; }

    /// <summary>Describes the origin of this score calculation (manual edit, scheduled job, calendar sync, etc.).</summary>
    public ScoreTriggerSource TriggerSource { get; set; }

    /// <summary>Deterministic one-line explanation of why the task scored this way (e.g. "Deadline in 6h, blocks 2 tasks"). Null on rows written before it existed.</summary>
    public string? ReasonText { get; set; }
}
