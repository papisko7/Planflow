using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// One entry of a user's in-app inbox ("what happened while I was away"). It is created in the same transaction as the
/// change that caused it, exactly like an e-mail outbox row; the same row also drives the push to the phone
/// (<see cref="PushState"/>), so the inbox and the push can never disagree.
/// TeamId / TaskId are plain ids without foreign keys on purpose: the entry must survive the deletion of the task or team it is about.
/// </summary>
public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public NotificationEvent Event { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Lets the app open the right screen when the entry is tapped.</summary>
    public Guid? TeamId { get; set; }
    public Guid? TaskId { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public PushDeliveryState PushState { get; set; }
    public int PushAttempts { get; set; }
    public DateTime PushNextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public string? PushLastError { get; set; }
}
