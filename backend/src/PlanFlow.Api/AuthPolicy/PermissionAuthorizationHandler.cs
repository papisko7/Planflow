using Microsoft.AspNetCore.Authorization;
using PlanFlow.Domain.Authorization;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Api.AuthPolicy;

/// <summary>
/// Reads the caller's team role from the "team_role" claim (set on the JWT at login/team-switch,
/// Phase 4.3) and grants access only if <see cref="RoleDefinitions"/> allows that role to perform
/// the requirement's permission. This keeps the actual RBAC rules in one Domain-layer table instead
/// of scattering role checks across every controller.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private const string TeamRoleClaimType = "team_role";

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var roleClaim = context.User.FindFirst(TeamRoleClaimType)?.Value;

        if (roleClaim is not null
            && Enum.TryParse<TeamRole>(roleClaim, ignoreCase: true, out var role)
            && RoleDefinitions.HasPermission(role, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
