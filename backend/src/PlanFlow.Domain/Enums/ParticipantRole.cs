namespace PlanFlow.Domain.Enums;

/// <summary>How a team member takes part in an item that is not theirs alone. The numbers are stored in the database: only append.</summary>
public enum ParticipantRole
{
    /// <summary>Invited to an event.</summary>
    Guest = 0,

    /// <summary>Works on a task together with its assignee.</summary>
    CoContractor = 1
}
