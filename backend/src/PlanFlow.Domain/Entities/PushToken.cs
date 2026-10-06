using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// The address of one phone for push notifications (an Expo push token). A token belongs to exactly one user at a time:
/// when somebody else signs in on the same phone the token moves to them, so pushes never reach the previous account.
/// </summary>
public class PushToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Token { get; set; } = string.Empty;

    /// <summary>"ios" or "android" — informational only.</summary>
    public string Platform { get; set; } = string.Empty;

    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}
