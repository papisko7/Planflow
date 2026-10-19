using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Notifications;

/// <summary>
/// Focus time is "do not disturb" for the phone. A push that is not the person's own alert waits until the Focus block
/// ends (the push job sends what is due); the inbox entry appears at once, and urgency alerts and item reminders break through.
/// </summary>
public class FocusTimeDoNotDisturbTests
{
    private sealed record Setup(PlanFlowDbContext Db, User Owner, User Anna, Team Team, NotificationService Service);

    private static async Task<Setup> SetUpAsync()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var anna = await TestDb.AddUserAsync(db, "Anna");
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (anna, TeamRole.Member));
        return new Setup(db, owner, anna, team, new NotificationService(db));
    }

    private static CalendarItem AddFocus(
        Setup s, User who, double startHoursFromNow, double endHoursFromNow, bool allDay = false, bool noEnd = false,
        TaskStatus status = TaskStatus.Confirmed)
    {
        var now = DateTime.UtcNow;
        var item = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = who.Id, Title = "Deep work", TaskType = TaskType.Event, Status = status,
            EventTemplate = EventTemplate.FocusTime, IsAllDay = allDay,
            StartUtc = now.AddHours(startHoursFromNow), EndUtc = noEnd ? null : now.AddHours(endHoursFromNow)
        };
        s.Db.CalendarItems.Add(item);
        s.Db.SaveChanges();
        return item;
    }

    private static Task Notify(Setup s, NotificationEvent evt) =>
        s.Service.NotifyAsync(evt, new[] { s.Anna.Id }, name => new EmailContent("Subject", "<p>Body</p>", "Body", "Preview"),
            s.Team.Id, s.Owner.Id, CancellationToken.None);

    private static Task<Notification> Saved(Setup s) =>
        Task.FromResult(s.Db.ChangeTracker.Entries<Notification>().Single().Entity);

    [Fact]
    public async Task Assignment_DuringFocusTime_PushWaitsUntilTheBlockEnds()
    {
        var s = await SetUpAsync();
        var focus = AddFocus(s, s.Anna, -1, 2);

        await Notify(s, NotificationEvent.TaskAssignedToYou);

        var entry = await Saved(s);
        Assert.Equal(PushDeliveryState.Pending, entry.PushState);
        Assert.Equal(focus.EndUtc!.Value, entry.PushNextAttemptAtUtc);
    }

    [Fact]
    public async Task Assignment_DuringFocusTime_InboxEntryIsStillMadeAtOnce()
    {
        var s = await SetUpAsync();
        AddFocus(s, s.Anna, -1, 2);

        await Notify(s, NotificationEvent.TaskAssignedToYou);

        Assert.Single(s.Db.ChangeTracker.Entries<Notification>());
    }

    [Theory]
    [InlineData(NotificationEvent.TaskUrgent)]
    [InlineData(NotificationEvent.ItemReminder)]
    public async Task OwnAlerts_BreakThroughFocusTime(NotificationEvent evt)
    {
        var s = await SetUpAsync();
        AddFocus(s, s.Anna, -1, 2);
        var before = DateTime.UtcNow.AddSeconds(-1);

        await Notify(s, evt);

        Assert.True((await Saved(s)).PushNextAttemptAtUtc <= DateTime.UtcNow.AddSeconds(1));
        Assert.True((await Saved(s)).PushNextAttemptAtUtc >= before);
    }

    [Fact]
    public async Task OutsideFocusTime_PushGoesOutNow()
    {
        var s = await SetUpAsync();
        AddFocus(s, s.Anna, 1, 3); // later today
        AddFocus(s, s.Anna, -5, -3); // earlier today

        await Notify(s, NotificationEvent.TaskAssignedToYou);

        Assert.True((await Saved(s)).PushNextAttemptAtUtc <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task SomeoneElsesFocusTime_DoesNotHoldBackThePush()
    {
        var s = await SetUpAsync();
        AddFocus(s, s.Owner, -1, 2);

        await Notify(s, NotificationEvent.TaskAssignedToYou);

        Assert.True((await Saved(s)).PushNextAttemptAtUtc <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task OverlappingFocusBlocks_WaitForTheLatestEnd()
    {
        var s = await SetUpAsync();
        AddFocus(s, s.Anna, -1, 1);
        var later = AddFocus(s, s.Anna, 0, 3);

        await Notify(s, NotificationEvent.TaskChanged);

        Assert.Equal(later.EndUtc!.Value, (await Saved(s)).PushNextAttemptAtUtc);
    }

    [Fact]
    public async Task FocusBlockWithoutAnEnd_IsAssumedToLastAnHour()
    {
        var s = await SetUpAsync();
        var focus = AddFocus(s, s.Anna, -0.5, 0, noEnd: true);

        await Notify(s, NotificationEvent.TaskAssignedToYou);

        Assert.Equal(focus.StartUtc!.Value.AddHours(1), (await Saved(s)).PushNextAttemptAtUtc);
    }

    [Fact]
    public async Task CancelledOrAllDayFocusTime_DoesNotCount()
    {
        var s = await SetUpAsync();
        AddFocus(s, s.Anna, -1, 2, status: TaskStatus.Cancelled);
        AddFocus(s, s.Anna, -1, 2, allDay: true);

        await Notify(s, NotificationEvent.TaskAssignedToYou);

        Assert.True((await Saved(s)).PushNextAttemptAtUtc <= DateTime.UtcNow.AddSeconds(1));
    }

    [Theory]
    [InlineData(NotificationEvent.TaskAssignedToYou, true)]
    [InlineData(NotificationEvent.TaskChanged, true)]
    [InlineData(NotificationEvent.TeamTaskAdded, true)]
    [InlineData(NotificationEvent.TaskReturnedToPool, true)]
    [InlineData(NotificationEvent.CalendarConnectionLost, true)]
    [InlineData(NotificationEvent.TaskUrgent, false)]
    [InlineData(NotificationEvent.ItemReminder, false)]
    [InlineData(NotificationEvent.PasswordReset, false)] // an account mail has no phone form at all
    [InlineData(NotificationEvent.ChatDigest, false)]
    public void Policy_SaysWhichPushesWaitForTheEndOfFocusTime(NotificationEvent evt, bool waits)
    {
        Assert.Equal(waits, NotificationPolicy.IsDeferredByFocusTime(evt));
    }
}
