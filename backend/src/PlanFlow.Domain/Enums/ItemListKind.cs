namespace PlanFlow.Domain.Enums;

/// <summary>What an own list of a team holds. The numbers are stored in the database: only append.</summary>
public enum ItemListKind
{
    /// <summary>Holds tasks.</summary>
    TaskList = 0,

    /// <summary>Holds events (meetings).</summary>
    MeetingList = 1
}
