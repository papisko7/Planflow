using System.Security.Claims;
using PlanFlow.Application.Common.Exceptions;

namespace PlanFlow.Api.Common;

/// <summary>Reads the identity claims set by <see cref="PlanFlow.Infrastructure.Security.JwtTokenService"/> off the current request's principal.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedException("Token is missing a user identity claim.");

        return Guid.Parse(value);
    }

    /// <summary>The team the caller's "team_role" claim applies to, or null if they logged in without a team context.</summary>
    public static Guid? GetTeamId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst("team_id")?.Value;
        return value is null ? null : Guid.Parse(value);
    }
}
