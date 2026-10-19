namespace PlanFlow.Domain.Enums;

/// <summary>
/// Role of a user within a team, used for RBAC enforcement. Numeric values are
/// persisted, so they stay stable even though the former Guest role (3) was removed.
/// </summary>
public enum TeamRole
{
    Owner = 0,
    Admin = 1,
    Member = 2
}
