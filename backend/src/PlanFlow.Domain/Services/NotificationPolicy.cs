using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Services;

/// <summary>
/// The single place that answers "does this user want this notification, and through which channel?". Pure and framework-free, so the rules
/// (mandatory mails, master switch, per-category switch, per-team mute) are easy to unit test and to explain.
/// </summary>
public static class NotificationPolicy
{
    /// <summary>Account recovery / security mails that cannot be turned off — without them a user could lock themselves out.</summary>
    public static bool IsMandatory(NotificationEvent evt) =>
        evt is NotificationEvent.Welcome or NotificationEvent.PasswordReset or NotificationEvent.PasswordChanged
            or NotificationEvent.EmailVerification;

    /// <summary>
    /// Optional mails go only to a confirmed address, so nobody can make PlanFlow mail a stranger by signing up with their
    /// e-mail. The mandatory account mails are exempt: they are how the address gets confirmed in the first place.
    /// </summary>
    public static bool CanReceive(bool isEmailVerified, NotificationEvent evt) => isEmailVerified || IsMandatory(evt);

    /// <summary>E-mail: mandatory mails always pass; the rest need the e-mail master switch and the category switch.</summary>
    public static bool IsAllowed(NotificationPreference prefs, NotificationEvent evt) =>
        IsMandatory(evt) || (HasEmailForm(evt) && prefs.EmailEnabled && IsCategoryEnabled(prefs, evt));

    /// <summary>The daily "items need details" reminder lives in the app only: a mail every morning would be spam.</summary>
    public static bool HasEmailForm(NotificationEvent evt) => evt != NotificationEvent.IncompleteItemsReminder;

    /// <summary>
    /// Whether this kind of event has an in-app form at all. Account/security mails carry codes and belong in the mailbox only,
    /// and the chat digest is a summary mail by nature (live chat has its own screen), so none of them go to the inbox or phone.
    /// </summary>
    public static bool HasInAppForm(NotificationEvent evt) =>
        !IsMandatory(evt) && evt is not (NotificationEvent.NewDeviceLogin or NotificationEvent.ChatDigest);

    /// <summary>Inbox entry: the category switch decides (there is no inbox master switch — it is just a list in the app).</summary>
    public static bool IsInboxAllowed(NotificationPreference prefs, NotificationEvent evt) =>
        HasInAppForm(evt) && IsCategoryEnabled(prefs, evt);

    /// <summary>Push to the phone: an inbox-worthy event plus the push master switch.</summary>
    public static bool IsPushAllowed(NotificationPreference prefs, NotificationEvent evt) =>
        prefs.PushEnabled && IsInboxAllowed(prefs, evt);

    private static bool IsCategoryEnabled(NotificationPreference prefs, NotificationEvent evt)
    {
        return evt switch
        {
            NotificationEvent.NewDeviceLogin => prefs.LoginAlerts != LoginNotificationMode.Off,
            NotificationEvent.TaskAssignedToYou or NotificationEvent.TaskUnassigned
                or NotificationEvent.ParticipantAdded or NotificationEvent.ParticipantRemoved => prefs.TaskAssignments,
            NotificationEvent.ItemReminder => prefs.ItemReminders,
            NotificationEvent.TaskChanged or NotificationEvent.TaskDeleted => prefs.TaskChanges,
            NotificationEvent.TeamTaskAdded => prefs.TeamTasks,
            NotificationEvent.TaskUrgent => prefs.TaskUrgency,
            NotificationEvent.AddedToTeam or NotificationEvent.TeamRoleChanged
                or NotificationEvent.RemovedFromTeam or NotificationEvent.TeamDeleted => prefs.TeamMembership,
            NotificationEvent.ChatDigest => prefs.ChatDigest,
            NotificationEvent.CalendarConnectionLost or NotificationEvent.CalendarItemsIncomplete => prefs.CalendarIssues,
            NotificationEvent.IncompleteItemsReminder => prefs.IncompleteItemsReminder,
            _ => false
        };
    }

    /// <summary>Whether a sign-in should produce an e-mail, given the user's login-alert mode and whether the device is new.</summary>
    public static bool ShouldAlertLogin(NotificationPreference prefs, bool isNewDevice) =>
        prefs.EmailEnabled && prefs.LoginAlerts switch
        {
            LoginNotificationMode.EveryLogin => true,
            LoginNotificationMode.NewDevice => isNewDevice,
            _ => false
        };

    /// <summary>
    /// Whether the user muted the team this e-mail is about. Muting a team silences its task and chat mails only —
    /// membership mails ("you were removed") must still arrive, because after a removal there is nothing left to mute.
    /// </summary>
    public static bool IsMutedByTeam(NotificationEvent evt, bool muteNotifications, bool muteChat) => evt switch
    {
        NotificationEvent.TaskAssignedToYou or NotificationEvent.TaskUnassigned or NotificationEvent.TaskChanged
            or NotificationEvent.TaskDeleted or NotificationEvent.TeamTaskAdded or NotificationEvent.TaskUrgent
            or NotificationEvent.ItemReminder or NotificationEvent.ParticipantAdded or NotificationEvent.ParticipantRemoved
            or NotificationEvent.IncompleteItemsReminder => muteNotifications,
        NotificationEvent.ChatDigest => muteChat,
        _ => false
    };
}
