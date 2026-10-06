using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>A device (identified by an id the app generates once) a user has signed in from — used to spot a "new device" sign-in.</summary>
public class UserDevice : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}
