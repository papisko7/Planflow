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

    // Tasks
    TaskAssignedToYou = 10,
    TaskUnassigned = 11,
    TaskChanged = 12,
    TaskDeleted = 13,
    TeamTaskAdded = 14,
    TaskUrgent = 15,

    // Team membership
    AddedToTeam = 20,
    TeamRoleChanged = 21,
    RemovedFromTeam = 22,
    TeamDeleted = 23,

    // Chat
    ChatDigest = 30,

    // Calendar
    CalendarConnectionLost = 40
}
