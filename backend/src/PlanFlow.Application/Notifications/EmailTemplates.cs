using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Notifications;

/// <summary>
/// The wording of every e-mail. Each method returns a builder that takes the recipient's name, so the same
/// template can be sent to many people (e.g. everybody in a team) with a personal greeting.
/// </summary>
public static class EmailTemplates
{
    // ---------- Account ----------

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

    // ---------- Tasks and events ----------
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

    /// <summary>"task" or "event": the word notifications use for an item of the given type.</summary>
    public static string Noun(TaskType type) => type == TaskType.Event ? "event" : "task";

    public static string Capitalized(TaskType type) => type == TaskType.Event ? "Event" : "Task";

    // An event has a start, not a deadline.
    private static string DueLabel(TaskType type) => type == TaskType.Event ? "Starts" : "Deadline";

    // ---------- Team membership ----------

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

    // ---------- Chat / calendar ----------

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

    // ---------- helpers ----------

    private static IReadOnlyList<string> Lines(params string?[] lines) => lines.Where(l => l is not null).Select(l => l!).ToList();

    private static string Format(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") + " UTC";

    /// <summary>Subjects are headers: a title containing line breaks must not be able to add extra header lines.</summary>
    private static string Clean(string value) => value.ReplaceLineEndings(" ").Trim();
}
