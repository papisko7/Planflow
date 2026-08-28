namespace PlanFlow.Domain.Authorization;

/// <summary>
/// A single action that can be allowed or denied for a team role. Kept in the Domain
/// layer because "what a role can do" is a business rule, not an infrastructure concern.
/// </summary>
public enum Permission
{
    ViewTasks,
    CreateTask,
    EditOwnTask,
    EditAnyTask,
    DeleteTask,
    ManageMembers,
    ManageTaskPolicies,
    DeleteTeam
}
