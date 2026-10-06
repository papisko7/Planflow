using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// One signed-in device of a user. Each login gets its own refresh token (stored only as a hash),
/// so signing in on a second device does not log the first one out.
/// </summary>
public class RefreshSession : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
