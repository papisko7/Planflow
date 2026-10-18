using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// One user's notification settings: a master switch per channel (e-mail, push) plus one switch per category.
/// A category switch decides whether that kind of event is wanted at all (inbox, push and e-mail alike).
/// A user without a row gets <see cref="CreateDefault"/>; the row is created lazily on the first save.
/// Per-team muting lives on <see cref="TeamMember"/> (MuteNotifications / MuteChat).
/// </summary>
public class NotificationPreference : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>E-mail master switch: off silences every e-mail except the mandatory account/security ones.</summary>
    public bool EmailEnabled { get; set; } = true;

    /// <summary>Master switch for push notifications on the phone. The in-app inbox is not affected by it.</summary>
    public bool PushEnabled { get; set; } = true;

    public LoginNotificationMode LoginAlerts { get; set; } = LoginNotificationMode.NewDevice;

    /// <summary>A task was assigned to you or taken away from you.</summary>
    public bool TaskAssignments { get; set; } = true;

    /// <summary>Someone else edited or deleted a task assigned to you.</summary>
    public bool TaskChanges { get; set; } = true;

    /// <summary>Any new task in your teams (off by default — in a big team this is noisy; your own assignments are covered above).</summary>
    public bool TeamTasks { get; set; }

    /// <summary>A task assigned to you became urgent (deadline within 24h or urgency spike).</summary>
    public bool TaskUrgency { get; set; } = true;

    /// <summary>You were added to / removed from a team, your role changed, or a team of yours was deleted.</summary>
    public bool TeamMembership { get; set; } = true;

    /// <summary>Summary of chat messages you missed while offline (off by default).</summary>
    public bool ChatDigest { get; set; }

    /// <summary>Chat messages sent before this moment count as already seen (the user was online, or got a digest covering them).</summary>
    public DateTime? ChatSeenUpToUtc { get; set; }

    /// <summary>When the last chat digest was e-mailed; digests are at most one per hour.</summary>
    public DateTime? LastChatDigestSentAtUtc { get; set; }

    /// <summary>Your Google Calendar connection stopped working.</summary>
    public bool CalendarIssues { get; set; } = true;

    /// <summary>A daily push about items that still need details (impact, end time). Inbox + push only, never e-mail.</summary>
    public bool IncompleteItemsReminder { get; set; } = true;

    /// <summary>When the last daily reminder was raised; at most one per local day, even if the user deleted the entry meanwhile.</summary>
    public DateTime? LastIncompleteReminderSentAtUtc { get; set; }

    public static NotificationPreference CreateDefault(Guid userId) => new() { UserId = userId };
}
