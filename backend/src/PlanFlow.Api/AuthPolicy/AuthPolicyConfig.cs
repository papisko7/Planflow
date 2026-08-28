using Microsoft.AspNetCore.Authorization;
using PlanFlow.Domain.Authorization;

namespace PlanFlow.Api.AuthPolicy;

/// <summary>
/// Registers one ASP.NET Core authorization policy per <see cref="Permission"/>, named after the
/// enum value (e.g. "DeleteTeam"), so endpoints can be locked down with
/// [Authorize(Policy = nameof(Permission.DeleteTeam))] as soon as controllers are added (Phase 1.4).
/// </summary>
public static class AuthPolicyConfig
{
    public static IServiceCollection AddRbacPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in Enum.GetValues<Permission>())
            {
                options.AddPolicy(permission.ToString(), policy =>
                    policy.Requirements.Add(new PermissionRequirement(permission)));
            }
        });

        return services;
    }
}
