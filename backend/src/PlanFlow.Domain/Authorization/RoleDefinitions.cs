using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Authorization;

/// <summary>
/// Single source of truth for the RBAC permission matrix (Role -> allowed Permissions).
/// Every authorization check, in the API or elsewhere, should go through <see cref="HasPermission"/>
/// instead of duplicating role checks, so the matrix only ever needs to change in one place.
/// </summary>
public static class RoleDefinitions
{
    private static readonly IReadOnlyDictionary<TeamRole, HashSet<Permission>> Matrix = new Dictionary<TeamRole, HashSet<Permission>>
    {
        [TeamRole.Owner] = new()
        {
            Permission.ViewTasks,
            Permission.CreateTask,
            Permission.EditOwnTask,
            Permission.EditAnyTask,
            Permission.DeleteTask,
            Permission.ManageMembers,
            Permission.ManageTaskPolicies,
            Permission.DeleteTeam
        },
        [TeamRole.Admin] = new()
        {
            Permission.ViewTasks,
            Permission.CreateTask,
            Permission.EditOwnTask,
            Permission.EditAnyTask,
            Permission.DeleteTask,
            Permission.ManageMembers,
            Permission.ManageTaskPolicies
        },
        [TeamRole.Member] = new()
        {
            Permission.ViewTasks,
            Permission.CreateTask,
            Permission.EditOwnTask
        },
        [TeamRole.Guest] = new()
        {
            Permission.ViewTasks
        }
    };

    public static bool HasPermission(TeamRole role, Permission permission) =>
        Matrix.TryGetValue(role, out var permissions) && permissions.Contains(permission);
}
