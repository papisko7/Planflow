using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Application;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>
/// The job that sends the alerts. A reminder goes off once per trigger moment, to the assignee, the participants and the
/// creator, through the people's own notification settings, and never for an item that is finished or for a moment long gone.
/// </summary>
public class ItemReminderJobTests
{
    // A Tuesday morning in October (Warsaw is UTC+2 until the 25th).
    private static readonly DateTime Start = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    private static ItemReminderJob Job(PlanFlowDbContext db) =>
        new(db, new NotificationService(db), Mock.Of<Microsoft.Extensions.Logging.ILogger<ItemReminderJob>>());

    private sealed record Setup(PlanFlowDbContext Db, User Ann, User Ben, User Olga, Team Team);

    private static async Task<Setup> SetUpAsync()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db, "Ann");
        var ben = await TestDb.AddUserAsync(db, "Ben");
        var olga = await TestDb.AddUserAsync(db, "Olga");
        var team = await TestDb.AddTeamAsync(db, (olga, TeamRole.Owner), (ann, TeamRole.Member), (ben, TeamRole.Member));
        return new Setup(db, ann, ben, olga, team);
    }

    private static async Task<CalendarItem> AddEventAsync(Setup s, ItemReminder reminder, Guid? assignee = null, DateTime? start = null, bool allDay = false)
    {
        var item = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = assignee ?? s.Ann.Id, Title = "Standup", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            StartUtc = start ?? Start, EndUtc = (start ?? Start).AddHours(allDay ? 24 : 1), IsAllDay = allDay
        };
        s.Db.CalendarItems.Add(item);
        reminder.CalendarItemId = item.Id;
        s.Db.ItemReminders.Add(reminder);
        await s.Db.SaveChangesAsync();
        return item;
    }

    // ---- when ----

    [Fact]
    public async Task Run_AtTheMoment_SendsPushInboxAndMail_ToTheAssignee()
    {
        var s = await SetUpAsync();
        var item = await AddEventAsync(s, ItemReminder.Offset(30));

        var fired = await Job(s.Db).RunAsync(Start.AddMinutes(-30), CancellationToken.None);

        Assert.Equal(1, fired);
        var entry = Assert.Single(s.Db.Notifications);
        Assert.Equal(NotificationEvent.ItemReminder, entry.Event);
        Assert.Equal(s.Ann.Id, entry.UserId);
        Assert.Equal(item.Id, entry.TaskId);
        Assert.Equal(PushDeliveryState.Pending, entry.PushState);
        Assert.Contains("\"Standup\" starts in 30 minutes", entry.Body);
        var mail = Assert.Single(s.Db.EmailOutbox);
        Assert.Equal(s.Ann.Email, mail.ToEmail);
        Assert.Contains("Reminder: Standup", mail.Subject);
    }

    [Fact]
    public async Task Run_BeforeTheMoment_SendsNothing()
    {
        var s = await SetUpAsync();
        await AddEventAsync(s, ItemReminder.Offset(30));

        var fired = await Job(s.Db).RunAsync(Start.AddMinutes(-31), CancellationToken.None);

        Assert.Equal(0, fired);
        Assert.Empty(s.Db.Notifications);
    }

    [Fact]
    public async Task Run_ReminderWhoseMomentIsLongGone_IsNotCaughtUp()
    {
        var s = await SetUpAsync();
        await AddEventAsync(s, ItemReminder.Offset(30));

        // 25 minutes after the moment (e.g. an item imported or created just now): silent.
        var fired = await Job(s.Db).RunAsync(Start.AddMinutes(-5), CancellationToken.None);

        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Run_SendsOncePerMoment_NotEveryMinute()
    {
        var s = await SetUpAsync();
        var item = await AddEventAsync(s, ItemReminder.Offset(30));
        var job = Job(s.Db);

        await job.RunAsync(Start.AddMinutes(-30), CancellationToken.None);
        var again = await job.RunAsync(Start.AddMinutes(-29), CancellationToken.None);

        Assert.Equal(0, again);
        Assert.Single(s.Db.Notifications);
        Assert.Equal(Start.AddMinutes(-30), (await s.Db.ItemReminders.SingleAsync(r => r.CalendarItemId == item.Id)).LastFiredForUtc);
    }

    [Fact]
    public async Task Run_WhenTheItemMoves_TheReminderIsDueAgainForTheNewMoment()
    {
        var s = await SetUpAsync();
        var item = await AddEventAsync(s, ItemReminder.Offset(30));
        var job = Job(s.Db);
        await job.RunAsync(Start.AddMinutes(-30), CancellationToken.None);

        item.StartUtc = Start.AddHours(3);
        await s.Db.SaveChangesAsync();
        var fired = await job.RunAsync(Start.AddHours(3).AddMinutes(-30), CancellationToken.None);

        Assert.Equal(1, fired);
        Assert.Equal(2, s.Db.Notifications.Count());
    }

    [Theory]
    [InlineData(TaskStatus.Cancelled)]
    [InlineData(TaskStatus.Completed)]
    public async Task Run_AFinishedItem_SendsNothing(TaskStatus status)
    {
        var s = await SetUpAsync();
        var item = await AddEventAsync(s, ItemReminder.Offset(30));
        item.Status = status;
        await s.Db.SaveChangesAsync();

        Assert.Equal(0, await Job(s.Db).RunAsync(Start.AddMinutes(-30), CancellationToken.None));
    }

    [Fact]
    public async Task Run_AnItemFarInTheFuture_IsNotEvenLookedAt()
    {
        var s = await SetUpAsync();
        await AddEventAsync(s, ItemReminder.Offset(30), start: Start.AddDays(90));

        Assert.Equal(0, await Job(s.Db).RunAsync(Start, CancellationToken.None));
    }

    // ---- anchors and forms ----

    [Fact]
    public async Task Run_ATaskIsRemindedBeforeItsDeadline()
    {
        var s = await SetUpAsync();
        var task = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = s.Ann.Id, Title = "Report", TaskType = TaskType.Task, Status = TaskStatus.Backlog,
            DeadlineUtc = Start.AddDays(2)
        };
        s.Db.CalendarItems.Add(task);
        s.Db.ItemReminders.Add(new ItemReminder { CalendarItemId = task.Id, Anchor = ReminderAnchor.Deadline, OffsetMinutes = 24 * 60 });
        await s.Db.SaveChangesAsync();

        var fired = await Job(s.Db).RunAsync(Start.AddDays(1), CancellationToken.None);

        Assert.Equal(1, fired);
        Assert.Contains("Task \"Report\" is due in 1 day", s.Db.Notifications.Single().Body);
    }

    [Fact]
    public async Task Run_ADayFormAlert_GoesOffAtThatTimeOnTheAssigneesClock()
    {
        var s = await SetUpAsync();
        s.Ann.TimeZoneId = "Europe/Warsaw";
        await s.Db.SaveChangesAsync();
        await AddEventAsync(s, ItemReminder.Day(1, 9 * 60)); // the 19th at 09:00 Warsaw = 07:00 UTC

        Assert.Equal(0, await Job(s.Db).RunAsync(new DateTime(2026, 10, 19, 6, 59, 0, DateTimeKind.Utc), CancellationToken.None));
        Assert.Equal(1, await Job(s.Db).RunAsync(new DateTime(2026, 10, 19, 7, 0, 0, DateTimeKind.Utc), CancellationToken.None));
        Assert.Contains("starts tomorrow", s.Db.Notifications.Single().Body);
    }

    [Fact]
    public async Task Run_AnAllDayEvent_ReadsItsDayLiterally_AndShowsTheDateOnly()
    {
        var s = await SetUpAsync();
        s.Ann.TimeZoneId = "Europe/Warsaw";
        await s.Db.SaveChangesAsync();
        var holiday = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        await AddEventAsync(s, ItemReminder.Day(1, 9 * 60), start: holiday, allDay: true); // the 21st at 09:00 Warsaw = 07:00 UTC

        var fired = await Job(s.Db).RunAsync(new DateTime(2026, 10, 21, 7, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        Assert.Equal(1, fired);
        Assert.Contains("2026-10-22 (all day)", s.Db.EmailOutbox.Single().TextBody);
        Assert.DoesNotContain("UTC", s.Db.EmailOutbox.Single().TextBody.Split('\n').First(l => l.Contains("Starts")));
    }

    // ---- who ----

    [Fact]
    public async Task Run_ReachesTheAssignee_TheParticipants_AndTheCreator_EachOnce()
    {
        var s = await SetUpAsync();
        var item = await AddEventAsync(s, ItemReminder.Offset(30));
        s.Db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = item.Id, UserId = s.Ben.Id, Role = ParticipantRole.Guest });
        s.Db.TaskHistories.Add(new TaskHistory { CalendarItemId = item.Id, ChangedByUserId = s.Olga.Id, ChangeType = TaskChangeType.Created });
        await s.Db.SaveChangesAsync();

        await Job(s.Db).RunAsync(Start.AddMinutes(-30), CancellationToken.None);

        Assert.Equal(
            new[] { s.Ann.Id, s.Ben.Id, s.Olga.Id }.OrderBy(i => i),
            s.Db.Notifications.Select(n => n.UserId).ToList().OrderBy(i => i));
    }

    [Fact]
    public async Task Run_WhenTheCreatorIsTheAssignee_TheyGetOneReminder()
    {
        var s = await SetUpAsync();
        var item = await AddEventAsync(s, ItemReminder.Offset(30));
        s.Db.TaskHistories.Add(new TaskHistory { CalendarItemId = item.Id, ChangedByUserId = s.Ann.Id, ChangeType = TaskChangeType.Created });
        await s.Db.SaveChangesAsync();

        await Job(s.Db).RunAsync(Start.AddMinutes(-30), CancellationToken.None);

        Assert.Single(s.Db.Notifications);
    }

    [Fact]
    public async Task Run_NobodyToTell_IsNotAnError_AndStillMarksItDone()
    {
        var s = await SetUpAsync();
        var item = await AddEventAsync(s, ItemReminder.Offset(30));
        item.AssignedUserId = null;
        await s.Db.SaveChangesAsync();

        var fired = await Job(s.Db).RunAsync(Start.AddMinutes(-30), CancellationToken.None);

        Assert.Equal(1, fired);
        Assert.Empty(s.Db.Notifications);
        Assert.NotNull((await s.Db.ItemReminders.SingleAsync()).LastFiredForUtc);
    }

    // ---- the people's settings ----

    [Fact]
    public async Task Run_APersonWhoSwitchedRemindersOff_GetsNeitherPushNorMail_OthersStillDo()
    {
        var s = await SetUpAsync();
        var item = await AddEventAsync(s, ItemReminder.Offset(30));
        s.Db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = item.Id, UserId = s.Ben.Id, Role = ParticipantRole.Guest });
        s.Db.NotificationPreferences.Add(new NotificationPreference { UserId = s.Ann.Id, ItemReminders = false });
        await s.Db.SaveChangesAsync();

        await Job(s.Db).RunAsync(Start.AddMinutes(-30), CancellationToken.None);

        Assert.Equal(s.Ben.Id, Assert.Single(s.Db.Notifications).UserId);
        Assert.Equal(s.Ben.Email, Assert.Single(s.Db.EmailOutbox).ToEmail);
    }

    [Fact]
    public async Task Run_PushOffButInboxOn_StillWritesTheInboxEntry()
    {
        var s = await SetUpAsync();
        await AddEventAsync(s, ItemReminder.Offset(30));
        s.Db.NotificationPreferences.Add(new NotificationPreference { UserId = s.Ann.Id, PushEnabled = false, EmailEnabled = false });
        await s.Db.SaveChangesAsync();

        await Job(s.Db).RunAsync(Start.AddMinutes(-30), CancellationToken.None);

        Assert.Equal(PushDeliveryState.NotWanted, Assert.Single(s.Db.Notifications).PushState);
        Assert.Empty(s.Db.EmailOutbox);
    }

    [Fact]
    public async Task Run_AMutedTeam_StaysSilent()
    {
        var s = await SetUpAsync();
        await AddEventAsync(s, ItemReminder.Offset(30));
        var membership = await s.Db.TeamMembers.SingleAsync(m => m.TeamId == s.Team.Id && m.UserId == s.Ann.Id);
        membership.MuteNotifications = true;
        await s.Db.SaveChangesAsync();

        await Job(s.Db).RunAsync(Start.AddMinutes(-30), CancellationToken.None);

        Assert.Empty(s.Db.Notifications);
        Assert.Empty(s.Db.EmailOutbox);
    }
}
