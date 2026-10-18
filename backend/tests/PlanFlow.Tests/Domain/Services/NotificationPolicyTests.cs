using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Domain.Services;

/// <summary>The rules for "does this user want this e-mail?" — pure logic, so every branch is checked directly.</summary>
public class NotificationPolicyTests
{
    private static NotificationPreference Defaults() => NotificationPreference.CreateDefault(Guid.NewGuid());

    [Theory]
    [InlineData(NotificationEvent.Welcome)]
    [InlineData(NotificationEvent.PasswordReset)]
    [InlineData(NotificationEvent.PasswordChanged)]
    public void MandatoryMails_AreSentEvenWhenEverythingIsSwitchedOff(NotificationEvent evt)
    {
        var prefs = Defaults();
        prefs.EmailEnabled = false;

        Assert.True(NotificationPolicy.IsMandatory(evt));
        Assert.True(NotificationPolicy.IsAllowed(prefs, evt));
    }

    [Theory]
    [InlineData(NotificationEvent.NewDeviceLogin)]
    [InlineData(NotificationEvent.TaskAssignedToYou)]
    [InlineData(NotificationEvent.TaskUnassigned)]
    [InlineData(NotificationEvent.TaskChanged)]
    [InlineData(NotificationEvent.TaskDeleted)]
    [InlineData(NotificationEvent.TaskUrgent)]
    [InlineData(NotificationEvent.AddedToTeam)]
    [InlineData(NotificationEvent.TeamRoleChanged)]
    [InlineData(NotificationEvent.RemovedFromTeam)]
    [InlineData(NotificationEvent.TeamDeleted)]
    [InlineData(NotificationEvent.CalendarConnectionLost)]
    [InlineData(NotificationEvent.CalendarItemsIncomplete)]
    public void MasterSwitchOff_SilencesEveryOptionalMail(NotificationEvent evt)
    {
        var prefs = Defaults();
        Assert.True(NotificationPolicy.IsAllowed(prefs, evt)); // on by default

        prefs.EmailEnabled = false;

        Assert.False(NotificationPolicy.IsAllowed(prefs, evt));
    }

    [Fact]
    public void DailyReminder_ReachesInboxAndPush_ButNeverTheMailbox()
    {
        var prefs = Defaults();
        var evt = NotificationEvent.IncompleteItemsReminder;

        Assert.False(NotificationPolicy.HasEmailForm(evt));
        Assert.False(NotificationPolicy.IsAllowed(prefs, evt)); // e-mail on, category on - still no mail
        Assert.True(NotificationPolicy.IsInboxAllowed(prefs, evt));
        Assert.True(NotificationPolicy.IsPushAllowed(prefs, evt));
    }

    [Fact]
    public void DailyReminder_HasItsOwnSwitch_ThatSilencesInboxAndPush()
    {
        var prefs = Defaults();
        prefs.IncompleteItemsReminder = false;

        Assert.False(NotificationPolicy.IsInboxAllowed(prefs, NotificationEvent.IncompleteItemsReminder));
        Assert.False(NotificationPolicy.IsPushAllowed(prefs, NotificationEvent.IncompleteItemsReminder));
        Assert.True(NotificationPolicy.IsInboxAllowed(prefs, NotificationEvent.CalendarItemsIncomplete)); // the import message is a separate category
    }

    [Fact]
    public void DailyReminder_ItemsOfAMutedTeamAreLeftOut()
    {
        Assert.True(NotificationPolicy.IsMutedByTeam(NotificationEvent.IncompleteItemsReminder, muteNotifications: true, muteChat: false));
        Assert.False(NotificationPolicy.IsMutedByTeam(NotificationEvent.IncompleteItemsReminder, muteNotifications: false, muteChat: true));
    }

    [Fact]
    public void Defaults_KeepNoisyMailsOff()
    {
        var prefs = Defaults();

        Assert.False(NotificationPolicy.IsAllowed(prefs, NotificationEvent.TeamTaskAdded));
        Assert.False(NotificationPolicy.IsAllowed(prefs, NotificationEvent.ChatDigest));
    }

    [Theory]
    [InlineData(NotificationEvent.TaskAssignedToYou)]
    [InlineData(NotificationEvent.TaskUnassigned)]
    public void AssignmentSwitch_ControlsBothAssignmentMails(NotificationEvent evt)
    {
        var prefs = Defaults();
        prefs.TaskAssignments = false;

        Assert.False(NotificationPolicy.IsAllowed(prefs, evt));
        Assert.True(NotificationPolicy.IsAllowed(prefs, NotificationEvent.TaskChanged)); // other categories unaffected
    }

    [Theory]
    [InlineData(NotificationEvent.TaskChanged)]
    [InlineData(NotificationEvent.TaskDeleted)]
    public void ChangeSwitch_ControlsEditAndDeleteMails(NotificationEvent evt)
    {
        var prefs = Defaults();
        prefs.TaskChanges = false;

        Assert.False(NotificationPolicy.IsAllowed(prefs, evt));
    }

    [Theory]
    [InlineData(NotificationEvent.AddedToTeam)]
    [InlineData(NotificationEvent.TeamRoleChanged)]
    [InlineData(NotificationEvent.RemovedFromTeam)]
    [InlineData(NotificationEvent.TeamDeleted)]
    public void MembershipSwitch_ControlsAllTeamMembershipMails(NotificationEvent evt)
    {
        var prefs = Defaults();
        prefs.TeamMembership = false;

        Assert.False(NotificationPolicy.IsAllowed(prefs, evt));
    }

    [Fact]
    public void OptInCategories_CanBeSwitchedOn()
    {
        var prefs = Defaults();
        prefs.TeamTasks = true;
        prefs.ChatDigest = true;

        Assert.True(NotificationPolicy.IsAllowed(prefs, NotificationEvent.TeamTaskAdded));
        Assert.True(NotificationPolicy.IsAllowed(prefs, NotificationEvent.ChatDigest));
    }

    [Theory]
    [InlineData(LoginNotificationMode.NewDevice, true, true)]
    [InlineData(LoginNotificationMode.NewDevice, false, false)]
    [InlineData(LoginNotificationMode.EveryLogin, true, true)]
    [InlineData(LoginNotificationMode.EveryLogin, false, true)]
    [InlineData(LoginNotificationMode.Off, true, false)]
    [InlineData(LoginNotificationMode.Off, false, false)]
    public void LoginAlert_FollowsTheChosenMode(LoginNotificationMode mode, bool isNewDevice, bool expected)
    {
        var prefs = Defaults();
        prefs.LoginAlerts = mode;

        Assert.Equal(expected, NotificationPolicy.ShouldAlertLogin(prefs, isNewDevice));
    }

    [Fact]
    public void LoginAlert_MasterSwitchOff_NeverAlerts()
    {
        var prefs = Defaults();
        prefs.LoginAlerts = LoginNotificationMode.EveryLogin;
        prefs.EmailEnabled = false;

        Assert.False(NotificationPolicy.ShouldAlertLogin(prefs, isNewDevice: true));
    }

    [Theory]
    [InlineData(NotificationEvent.TaskAssignedToYou, true, false, true)]
    [InlineData(NotificationEvent.TaskChanged, true, false, true)]
    [InlineData(NotificationEvent.TaskUrgent, true, false, true)]
    [InlineData(NotificationEvent.TaskChanged, false, true, false)]   // chat mute does not hide task mails
    [InlineData(NotificationEvent.ChatDigest, false, true, true)]
    [InlineData(NotificationEvent.ChatDigest, true, false, false)]    // task mute does not hide chat mails
    [InlineData(NotificationEvent.RemovedFromTeam, true, true, false)] // membership mails are never muted
    [InlineData(NotificationEvent.AddedToTeam, true, true, false)]
    public void TeamMute_SilencesOnlyTheMatchingKindOfMail(NotificationEvent evt, bool muteNotifications, bool muteChat, bool expectedMuted)
    {
        Assert.Equal(expectedMuted, NotificationPolicy.IsMutedByTeam(evt, muteNotifications, muteChat));
    }
}
