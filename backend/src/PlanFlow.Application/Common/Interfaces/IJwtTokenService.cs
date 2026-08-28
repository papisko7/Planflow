using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Common.Interfaces;

/// <summary>
/// Issues JWT access tokens and opaque refresh tokens. Implemented in Infrastructure (Phase 1.4)
/// so Application handlers never touch a JWT/crypto library directly.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// <paramref name="teamRole"/>/<paramref name="teamId"/> are only set when the caller logged in
    /// with a team context; the resulting "team_role"/"team_id" claims drive <c>PermissionAuthorizationHandler</c>.
    /// </summary>
    (string AccessToken, DateTime ExpiresAtUtc) GenerateAccessToken(User user, TeamRole? teamRole, Guid? teamId);

    (string RefreshToken, DateTime ExpiresAtUtc) GenerateRefreshToken();
}
