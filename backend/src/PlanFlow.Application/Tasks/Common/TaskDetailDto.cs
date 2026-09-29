using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Snapshot of one <see cref="UrgencyScoreLog"/> entry, exposed so the mobile dashboard can
/// render the "why is this urgent" breakdown UI planned for Phase 3.4.
/// </summary>
public record UrgencyScoreBreakdownDto(
    double DeadlineComponent,
    double AiComponent,
    double BlockingComponent,
    double ImpactComponent,
    double UserOverrideComponent,
    double FinalScore,
    bool AiFallbackUsed,
    ScoreTriggerSource TriggerSource,
    DateTime CreatedAtUtc)
{
    public static UrgencyScoreBreakdownDto FromEntity(UrgencyScoreLog log) => new(
        log.DeadlineComponent,
        log.AiComponent,
        log.BlockingComponent,
        log.ImpactComponent,
        log.UserOverrideComponent,
        log.FinalScore,
        log.AiFallbackUsed,
        log.TriggerSource,
        log.CreatedAtUtc);
}

/// <summary>Full detail view for a single task: the task itself plus its latest urgency breakdown.</summary>
public record TaskDetailDto(TaskDto Task, UrgencyScoreBreakdownDto? LatestScoreBreakdown);
