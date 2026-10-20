using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Notifications;

/// <summary>
/// The wording of every e-mail. Each method returns a builder that takes the recipient's name, so the same
/// template can be sent to many people (e.g. everybody in a team) with a personal greeting.
/// </summary>
public static class EmailTemplates
{
    /// <summary>The first mail after signing up: a greeting plus the code that confirms the e-mail address.</summary>
    public static Func<string, EmailContent> Welcome(string verificationCode, int validMinutes) => name => EmailLayout.Build(
        "Welcome to PlanFlow — confirm your e-mail",
        name,
        new[]
        {
            "Your PlanFlow account is ready. PlanFlow ranks your tasks by urgency, so you always know what to do next.",
            $"Enter this code in the app to confirm your e-mail address. It is valid for {validMinutes} minutes.",
            "If you did not create this account, you can ignore this e-mail."
        },
        highlight: verificationCode,
        mandatory: true);

    public static Func<string, EmailContent> VerifyEmailCode(string code, int validMinutes) => name => EmailLayout.Build(
        "Your PlanFlow e-mail confirmation code",
        name,
        new[]
        {
            $"Enter this code in the app to confirm your e-mail address. It is valid for {validMinutes} minutes and works once.",
            "If you did not ask for this, ignore this e-mail."
        },
        highlight: code,
        mandatory: true);

    public static Func<string, EmailContent> PasswordResetCode(string code, int validMinutes) => name => EmailLayout.Build(
        "Your PlanFlow password reset code",
        name,
        new[]
        {
            $"Use this code in the app to choose a new password. It is valid for {validMinutes} minutes and works once.",
            "If you did not ask for a reset, ignore this e-mail — your password has not been changed."
        },
        highlight: code,
        mandatory: true);

    /// <param name="signedOutEverywhere">True after a reset (every device is signed out); false after a change from a signed-in device (that device stays in).</param>
    public static Func<string, EmailContent> PasswordChanged(bool signedOutEverywhere = true) => name => EmailLayout.Build(
        "Your PlanFlow password was changed",
        name,
        new[]
        {
            signedOutEverywhere
                ? "The password of your PlanFlow account was just changed and you were signed out on all devices."
                : "The password of your PlanFlow account was just changed and all other devices were signed out.",
            "If this was not you, reset your password again right away."
        },
        mandatory: true);

    public static Func<string, EmailContent> NewDeviceLogin(string deviceName, DateTime whenUtc) => name => EmailLayout.Build(
        "New sign-in to your PlanFlow account",
        name,
        new[]
        {
            $"Your account was signed in to from \"{Clean(deviceName)}\" on {Format(whenUtc)}.",
            "If this was you, nothing to do. If not, change your password."
        });

    // One CalendarItem, two kinds: the wording says "task" or "event" to match what the recipient actually got.

    public static Func<string, EmailContent> TaskAssigned(string taskTitle, string teamName, string byName, DateTime? dueUtc, TaskType type = TaskType.Task) => name => EmailLayout.Build(
        $"New {Noun(type)} for you: {Clean(taskTitle)}",
        name,
        Lines(
            $"{byName} assigned the {Noun(type)} \"{taskTitle}\" to you in the team \"{teamName}\".",
            dueUtc is null ? null : $"{DueLabel(type)}: {Format(dueUtc.Value)}."));

    public static Func<string, EmailContent> TaskUnassigned(string taskTitle, string teamName, string byName, TaskType type = TaskType.Task) => name => EmailLayout.Build(
        $"{Capitalized(type)} taken off you: {Clean(taskTitle)}",
        name,
        Lines($"{byName} unassigned you from the {Noun(type)} \"{taskTitle}\" in the team \"{teamName}\"."));

    public static Func<string, EmailContent> TaskReturnedToPool(string taskTitle, string teamName, string byName, TaskType type = TaskType.Task) => name => EmailLayout.Build(
        $"{Capitalized(type)} back in the pool: {Clean(taskTitle)}",
        name,
        Lines($"{byName} gave the {Noun(type)} \"{taskTitle}\" in the team \"{teamName}\" back - it has no assignee now."));

    public static Func<string, EmailContent> TaskChanged(string taskTitle, string teamName, string byName, IReadOnlyList<string> changes, TaskType type = TaskType.Task) => name => EmailLayout.Build(
        $"Your {Noun(type)} was changed: {Clean(taskTitle)}",
        name,
        Lines(
            $"{byName} changed your {Noun(type)} \"{taskTitle}\" in the team \"{teamName}\".",
            changes.Count == 0 ? null : "What changed: " + string.Join("; ", changes) + "."));

    public static Func<string, EmailContent> TaskDeleted(string taskTitle, string teamName, string byName, TaskType type = TaskType.Task) => name => EmailLayout.Build(
        $"Your {Noun(type)} was deleted: {Clean(taskTitle)}",
        name,
        Lines($"{byName} deleted your {Noun(type)} \"{taskTitle}\" from the team \"{teamName}\"."));

    public static Func<string, EmailContent> TeamTaskAdded(string taskTitle, string teamName, string byName, TaskType type = TaskType.Task) => name => EmailLayout.Build(
        $"New {Noun(type)} in {Clean(teamName)}: {Clean(taskTitle)}",
        name,
        Lines($"{byName} added the {Noun(type)} \"{taskTitle}\" to the team \"{teamName}\"."));

    public static Func<string, EmailContent> TaskUrgent(string taskTitle, DateTime? dueUtc, double urgencyScore, TaskType type = TaskType.Task) => name => EmailLayout.Build(
        $"Urgent: {Clean(taskTitle)}",
        name,
        Lines(
            $"Your {Noun(type)} \"{taskTitle}\" is now very urgent (urgency {urgencyScore:P0}).",
            dueUtc is null ? null : $"{DueLabel(type)}: {Format(dueUtc.Value)}."));

    /// <summary>
    /// One of an item's own alerts went off. The first paragraph is what the push and the inbox show, so it says what is coming and when
    /// ("Event "Standup" starts in 30 minutes"); the second gives the exact moment (a whole-day item shows its date only).
    /// </summary>
    public static Func<string, EmailContent> ItemReminder(
        string title, TaskType type, ReminderAnchor anchor, DateTime anchorUtc, bool isAllDay,
        int? offsetMinutes, int? daysBefore) => name => EmailLayout.Build(
        $"Reminder: {Clean(title)}",
        name,
        Lines(
            $"{Capitalized(type)} \"{title}\" {ReminderVerb(type, anchor)} {ReminderLead(offsetMinutes, daysBefore)}.",
            $"{ReminderMomentLabel(type, anchor)}: {(isAllDay ? anchorUtc.ToString("yyyy-MM-dd") + " (all day)" : Format(anchorUtc))}."));

    public static Func<string, EmailContent> ParticipantAdded(string title, string teamName, string byName, DateTime? dueUtc, TaskType type, bool isAllDay = false) => name => EmailLayout.Build(
        $"You were added to the {Noun(type)}: {Clean(title)}",
        name,
        Lines(
            $"{byName} added you as a {RoleWord(type)} to the {Noun(type)} \"{title}\" in the team \"{teamName}\".",
            dueUtc is null ? null : $"{DueLabel(type)}: {(isAllDay ? dueUtc.Value.ToString("yyyy-MM-dd") + " (all day)" : Format(dueUtc.Value))}."));

    public static Func<string, EmailContent> ParticipantRemoved(string title, string teamName, string byName, TaskType type) => name => EmailLayout.Build(
        $"You were taken off the {Noun(type)}: {Clean(title)}",
        name,
        Lines($"{byName} removed you as a {RoleWord(type)} from the {Noun(type)} \"{title}\" in the team \"{teamName}\"."));

    /// <summary>A guest of an event, a co-contractor of a task.</summary>
    public static string RoleWord(TaskType type) => type == TaskType.Event ? "guest" : "co-contractor";

    private static string ReminderVerb(TaskType type, ReminderAnchor anchor) =>
        anchor == ReminderAnchor.Deadline ? "is due" : type == TaskType.Event ? "starts" : "is planned to start";

    private static string ReminderMomentLabel(TaskType type, ReminderAnchor anchor) =>
        anchor == ReminderAnchor.Deadline ? "Deadline" : type == TaskType.Event ? "Starts" : "Planned start";

    /// <summary>"in 30 minutes", "in 2 hours", "in 3 days", "tomorrow", "today": how far ahead a reminder looks, in the words of the form that made it.</summary>
    public static string ReminderLead(int? offsetMinutes, int? daysBefore)
    {
        if (offsetMinutes is { } minutes)
        {
            return minutes switch
            {
                0 => "now",
                _ when minutes % (24 * 60) == 0 => Plural(minutes / (24 * 60), "day"),
                _ when minutes % 60 == 0 => Plural(minutes / 60, "hour"),
                _ => Plural(minutes, "minute")
            };
        }

        return daysBefore switch
        {
            0 or null => "today",
            1 => "tomorrow",
            var days => $"in {days} days"
        };

        static string Plural(int count, string unit) => $"in {count} {unit}{(count == 1 ? "" : "s")}";
    }

    /// <summary>"task" or "event": the word notifications use for an item of the given type.</summary>
    public static string Noun(TaskType type) => type == TaskType.Event ? "event" : "task";

    public static string Capitalized(TaskType type) => type == TaskType.Event ? "Event" : "Task";

    private static string DueLabel(TaskType type) => type == TaskType.Event ? "Starts" : "Deadline";

    public static Func<string, EmailContent> AddedToTeam(string teamName, TeamRole role, string byName) => name => EmailLayout.Build(
        $"You were added to {Clean(teamName)}",
        name,
        Lines($"{byName} added you to the team \"{teamName}\" as {role}."));

    public static Func<string, EmailContent> TeamRoleChanged(string teamName, TeamRole oldRole, TeamRole newRole, string byName) => name => EmailLayout.Build(
        $"Your role in {Clean(teamName)} changed",
        name,
        Lines($"{byName} changed your role in the team \"{teamName}\" from {oldRole} to {newRole}."));

    public static Func<string, EmailContent> RemovedFromTeam(string teamName, string byName) => name => EmailLayout.Build(
        $"You were removed from {Clean(teamName)}",
        name,
        Lines(
            $"{byName} removed you from the team \"{teamName}\".",
            "Tasks and events that were assigned to you in that team are now unassigned."));

    public static Func<string, EmailContent> TeamDeleted(string teamName, string byName) => name => EmailLayout.Build(
        $"Team {Clean(teamName)} was deleted",
        name,
        Lines($"{byName} deleted the team \"{teamName}\" together with its tasks, events and chat."));

    public static Func<string, EmailContent> ChatDigest(IReadOnlyList<(string TeamName, int Count)> teams) => name => EmailLayout.Build(
        "You have unread chat messages",
        name,
        teams.Select(t => $"{t.Count} new message{(t.Count == 1 ? "" : "s")} in \"{t.TeamName}\".")
            .Append("Open PlanFlow to read them.")
            .ToList());

    public static Func<string, EmailContent> CalendarConnectionLost(string accountEmail) => name => EmailLayout.Build(
        "Your Google Calendar connection stopped working",
        name,
        Lines(
            $"PlanFlow can no longer read the Google Calendar of {accountEmail}, so new events are not being imported.",
            "Open Settings → Google Calendar and connect it again."));

    /// <summary>How many names the message lists before it says "and N more" — a first import can bring dozens of events.</summary>
    private const int MaxListedTitles = 10;

    /// <summary>
    /// Sent after a Google Calendar import left some events without the data the ranking needs (no impact, no effort, no end time).
    /// The first paragraph (it becomes the inbox/push text) lists the events by name; the second says how to fix them.
    /// </summary>
    public static Func<string, EmailContent> CalendarItemsIncomplete(IReadOnlyList<string> titles) => name =>
    {
        var subject = titles.Count == 1
            ? "1 imported event needs your details"
            : $"{titles.Count} imported events need your details";

        return EmailLayout.Build(
            subject,
            name,
            Lines(
                $"Imported from Google Calendar without enough data to rank {(titles.Count == 1 ? "it" : "them")}: {ListTitles(titles)}.",
                "Open each one in PlanFlow and fill in its impact and effort — saving it clears this reminder. " +
                "You can also add a line like \"[planflow] impact=8 effort=2h\" to the event's description in Google Calendar."));
    };

    /// <summary>
    /// The Google Tasks twin of <see cref="CalendarItemsIncomplete"/>: a task import brings only a title and a due day, so every new task
    /// waits for its impact and effort before it can be ranked properly.
    /// </summary>
    public static Func<string, EmailContent> GoogleTasksIncomplete(IReadOnlyList<string> titles) => name =>
    {
        var subject = titles.Count == 1
            ? "1 imported task needs your details"
            : $"{titles.Count} imported tasks need your details";

        return EmailLayout.Build(
            subject,
            name,
            Lines(
                $"Imported from Google Tasks without enough data to rank {(titles.Count == 1 ? "it" : "them")}: {ListTitles(titles)}.",
                "Open each one in PlanFlow and fill in its impact and effort — saving it clears this reminder. " +
                "You can also add a line like \"[planflow] impact=8 effort=2h\" to the task's details in Google Tasks."));
    };

    /// <summary>
    /// The once-a-day reminder (inbox + push only): everything the user still has to complete, not just what the last import brought.
    /// The first paragraph is what the notification shows, so it carries the names.
    /// </summary>
    public static Func<string, EmailContent> IncompleteItemsReminder(IReadOnlyList<string> titles) => name => EmailLayout.Build(
        titles.Count == 1 ? "1 item needs your details" : $"{titles.Count} items need your details",
        name,
        Lines(
            $"Waiting for details so {(titles.Count == 1 ? "it" : "they")} can be ranked: {ListTitles(titles)}.",
            "Open each one in PlanFlow and fill in its impact and effort — saving it clears this reminder."));

    private static string ListTitles(IReadOnlyList<string> titles)
    {
        var listed = string.Join(", ", titles.Take(MaxListedTitles).Select(t => $"\"{Clean(t)}\""));
        var more = titles.Count > MaxListedTitles ? $" and {titles.Count - MaxListedTitles} more" : "";
        return listed + more;
    }

    private static IReadOnlyList<string> Lines(params string?[] lines) => lines.Where(l => l is not null).Select(l => l!).ToList();

    private static string Format(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") + " UTC";

    /// <summary>Subjects are headers: a title containing line breaks must not be able to add extra header lines.</summary>
    private static string Clean(string value) => value.ReplaceLineEndings(" ").Trim();
}
