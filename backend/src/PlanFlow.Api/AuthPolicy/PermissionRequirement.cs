using Microsoft.AspNetCore.Authorization;
using PlanFlow.Domain.Authorization;

namespace PlanFlow.Api.AuthPolicy;

/// <summary>
/// Wraps a single <see cref="Permission"/> as an ASP.NET Core authorization requirement,
/// so controllers can just write [Authorize(Policy = nameof(Permission.EditAnyTask))].
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public Permission Permission { get; }

    public PermissionRequirement(Permission permission)
    {
        Permission = permission;
    }
}
