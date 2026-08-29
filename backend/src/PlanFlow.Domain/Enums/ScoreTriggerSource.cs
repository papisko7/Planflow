namespace PlanFlow.Domain.Enums;

/// <summary>
/// Describes the origin of an Urgency Score calculation, enabling audit trails for score changes.
/// </summary>
public enum ScoreTriggerSource
{
    ManualCreate = 0,
    ManualUpdate = 1,
    ScheduledRecalculation = 2,
    AiAssessment = 3,
    AiFallback = 4
}
