using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// A user's linked external calendar (Google OAuth2). Tokens must be encrypted at rest
/// by the Infrastructure layer; the Domain only models the shape of the data.
/// </summary>
public class CalendarIntegration : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public CalendarProvider Provider { get; set; } = CalendarProvider.Google;

    /// <summary>Stable id of the external account/calendar (e.g. Google's account id), used as the sync idempotency key.</summary>
    public string ExternalAccountId { get; set; } = string.Empty;
    public string ExternalAccountEmail { get; set; } = string.Empty;
    public string EncryptedAccessToken { get; set; } = string.Empty;
    public string EncryptedRefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; set; }

    public DateTime? LastSyncedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}
