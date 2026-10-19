namespace PlanFlow.Domain.Enums;

/// <summary>The unit of a repeat rule: "every <c>Interval</c> days / weeks / months / years". Plain int on the wire.</summary>
public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3
}
