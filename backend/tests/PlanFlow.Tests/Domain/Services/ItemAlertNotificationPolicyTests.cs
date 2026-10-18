using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

/// <summary>Step 7.5.6: the reminders have their own switch; joining / leaving an item follows the assignment switch; and the wording of a reminder.</summary>
public class ItemAlertNotificationPolicyTests
{
    private static NotificationPreference Defaults() => NotificationPreference.CreateDefault(Guid.NewGuid());

    [Fact]
    public void Reminders_AreOnByDefault_OnEveryChannel()
    {
        var prefs = Defaults();

        Assert.True(NotificationPolicy.HasEmailForm(NotificationEvent.ItemReminder));
        Assert.True(NotificationPolicy.IsAllowed(prefs, NotificationEvent.ItemReminder));
        Assert.True(NotificationPolicy.IsInboxAllowed(prefs, NotificationEvent.ItemReminder));
        Assert.True(NotificationPolicy.IsPushAllowed(prefs, NotificationEvent.ItemReminder));
    }

    [Fact]
    public void Reminders_HaveTheirOwnSwitch_ThatSilencesMailInboxAndPush_AndNothingElse()
    {
        var prefs = Defaults();
        prefs.ItemReminders = false;

        Assert.False(NotificationPolicy.IsAllowed(prefs, NotificationEvent.ItemReminder));
        Assert.False(NotificationPolicy.IsInboxAllowed(prefs, NotificationEvent.ItemReminder));
        Assert.False(NotificationPolicy.IsPushAllowed(prefs, NotificationEvent.ItemReminder));
        Assert.True(NotificationPolicy.IsAllowed(prefs, NotificationEvent.TaskUrgent));
        Assert.True(NotificationPolicy.IsAllowed(prefs, NotificationEvent.ParticipantAdded));
    }

    [Theory]
    [InlineData(NotificationEvent.ParticipantAdded)]
    [InlineData(NotificationEvent.ParticipantRemoved)]
    public void JoiningAndLeavingAnItem_FollowTheAssignmentSwitch(NotificationEvent evt)
    {
        var prefs = Defaults();
        Assert.True(NotificationPolicy.IsAllowed(prefs, evt));
        Assert.True(NotificationPolicy.IsPushAllowed(prefs, evt));

        prefs.TaskAssignments = false;

        Assert.False(NotificationPolicy.IsAllowed(prefs, evt));
        Assert.False(NotificationPolicy.IsInboxAllowed(prefs, evt));
        Assert.True(NotificationPolicy.IsAllowed(prefs, NotificationEvent.ItemReminder));
    }

    [Theory]
    [InlineData(NotificationEvent.ItemReminder)]
    [InlineData(NotificationEvent.ParticipantAdded)]
    [InlineData(NotificationEvent.ParticipantRemoved)]
    public void AMutedTeam_SilencesThem(NotificationEvent evt)
    {
        Assert.True(NotificationPolicy.IsMutedByTeam(evt, muteNotifications: true, muteChat: false));
        Assert.False(NotificationPolicy.IsMutedByTeam(evt, muteNotifications: false, muteChat: true));
    }

    [Theory]
    [InlineData(NotificationEvent.ItemReminder)]
    [InlineData(NotificationEvent.ParticipantAdded)]
    [InlineData(NotificationEvent.ParticipantRemoved)]
    public void TheyAreAlsoInTheInboxAndOnThePhone(NotificationEvent evt)
    {
        Assert.True(NotificationPolicy.HasInAppForm(evt));
        Assert.False(NotificationPolicy.IsMandatory(evt));
    }

    // ---- wording ----

    [Theory]
    [InlineData(0, null, "now")]
    [InlineData(1, null, "in 1 minute")]
    [InlineData(30, null, "in 30 minutes")]
    [InlineData(60, null, "in 1 hour")]
    [InlineData(120, null, "in 2 hours")]
    [InlineData(90, null, "in 90 minutes")]
    [InlineData(1440, null, "in 1 day")]
    [InlineData(2880, null, "in 2 days")]
    [InlineData(null, 0, "today")]
    [InlineData(null, 1, "tomorrow")]
    [InlineData(null, 3, "in 3 days")]
    public void ReminderLead_SaysHowFarAheadInTheWordsOfTheForm(int? offsetMinutes, int? daysBefore, string expected)
    {
        Assert.Equal(expected, EmailTemplates.ReminderLead(offsetMinutes, daysBefore));
    }

    [Fact]
    public void ReminderMail_ForATimedEvent_SaysWhatIsComingAndTheExactMoment()
    {
        var mail = EmailTemplates.ItemReminder(
            "Standup", TaskType.Event, ReminderAnchor.Start, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc), false, 30, null)("Ann");

        Assert.Equal("Reminder: Standup", mail.Subject);
        Assert.Equal("Event \"Standup\" starts in 30 minutes.", mail.Preview);
        Assert.Contains("Starts: 2026-10-20 10:00 UTC.", mail.TextBody);
    }

    [Fact]
    public void ReminderMail_ForATaskDeadline_SaysItIsDue()
    {
        var mail = EmailTemplates.ItemReminder(
            "Report", TaskType.Task, ReminderAnchor.Deadline, new DateTime(2026, 10, 25, 17, 0, 0, DateTimeKind.Utc), false, null, 1)("Ann");

        Assert.Equal("Task \"Report\" is due tomorrow.", mail.Preview);
        Assert.Contains("Deadline: 2026-10-25 17:00 UTC.", mail.TextBody);
    }

    [Fact]
    public void ReminderMail_ForATasksPlannedStart_SaysItIsPlannedToStart()
    {
        var mail = EmailTemplates.ItemReminder(
            "Report", TaskType.Task, ReminderAnchor.Start, new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc), true, null, 0)("Ann");

        Assert.Equal("Task \"Report\" is planned to start today.", mail.Preview);
        Assert.Contains("Planned start: 2026-10-22 (all day).", mail.TextBody);
    }

    [Fact]
    public void ParticipantMails_UseTheRightRoleWord()
    {
        var added = EmailTemplates.ParticipantAdded("Retro", "Team A", "Olga", null, TaskType.Event)("Ben");
        var removed = EmailTemplates.ParticipantRemoved("Report", "Team A", "Olga", TaskType.Task)("Ben");

        Assert.Contains("as a guest to the event \"Retro\"", added.Preview);
        Assert.Contains("as a co-contractor from the task \"Report\"", removed.Preview);
    }
}
