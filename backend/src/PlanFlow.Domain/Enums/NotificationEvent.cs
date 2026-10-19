namespace PlanFlow.Domain.Enums;

/// <summary>Every kind of e-mail PlanFlow can send. <see cref="Services.NotificationPolicy"/> decides which ones a user receives.</summary>
public enum NotificationEvent
{
    // Account — Welcome, PasswordReset, PasswordChanged and EmailVerification are always sent (security / account recovery).
    Welcome = 0,
    PasswordReset = 1,
    PasswordChanged = 2,
    NewDeviceLogin = 3,
    EmailVerification = 4,

    TaskAssignedToYou = 10,
    TaskUnassigned = 11,
    TaskChanged = 12,
    TaskDeleted = 13,
    TeamTaskAdded = 14,
    TaskUrgent = 15,

    /// <summary>One of an item's own reminders ("30 minutes before the start") went off.</summary>
    ItemReminder = 16,

    /// <summary>You were invited to an event as a guest / to a task as a co-contractor.</summary>
    ParticipantAdded = 17,

    /// <summary>You were taken off an event / task you took part in as a guest / co-contractor.</summary>
    ParticipantRemoved = 18,

    /// <summary>A Member gave a task they had taken back to the unassigned pool; sent to the team's Owners and Admins.</summary>
    TaskReturnedToPool = 19,

    AddedToTeam = 20,
    TeamRoleChanged = 21,
    RemovedFromTeam = 22,
    TeamDeleted = 23,

    ChatDigest = 30,

    CalendarConnectionLost = 40,
    CalendarItemsIncomplete = 41,

    /// <summary>The once-a-day push + inbox reminder about items that still lack data. Never e-mailed.</summary>
    IncompleteItemsReminder = 42
}
