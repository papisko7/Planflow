namespace PlanFlow.Domain.Enums;

/// <summary>
/// Role of a user within a team, used for RBAC enforcement (Phase 0.5).
/// </summary>
public enum TeamRole
{
    Owner = 0,
    Admin = 1,
    Member = 2,
    Guest = 3
}
