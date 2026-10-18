using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Application;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>
/// The daily "items need your details" reminder: one living inbox row per user, at most once per local day, only inside the
/// user's morning-to-evening window, never by e-mail and never when there is nothing to complete.
/// </summary>
public class IncompleteItemsReminderJobTests
{
    // 10:00 UTC on a Tuesday: inside the window for UTC users, 12:00 in Warsaw (UTC+2 until the end of October).
    private static readonly DateTime Morning = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    private static IncompleteItemsReminderJob Job(PlanFlowDbContext db) =>
        new(db, Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<IncompleteItemsReminderJob>>());

    private static async Task<(PlanFlowDbContext Db, User Ann, Team Team)> SetUpAsync()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db, "Ann");
        var team = await TestDb.AddTeamAsync(db, (ann, TeamRole.Owner));
        return (db, ann, team);
    }

    private static CalendarItem Incomplete(Team team, User user, string title, TaskType type = TaskType.Event, DateTime? endUtc = null)
    {
        var start = Morning.AddDays(1);
        return new CalendarItem
        {
            TeamId = team.Id, AssignedUserId = user.Id, Title = title, TaskType = type,
            Status = type == TaskType.Event ? TaskStatus.Confirmed : TaskStatus.Backlog,
            StartUtc = type == TaskType.Event ? start : null,
            EndUtc = type == TaskType.Event ? endUtc ?? start.AddHours(1) : null,
            DeadlineUtc = type == TaskType.Task ? start : null,
            IsDataIncomplete = true
        };
    }

    [Fact]
    public async Task Run_ListsEveryIncompleteItem_InOneInboxEntryWithAPush_AndNoMail()
    {
        var (db, ann, team) = await SetUpAsync();
        db.CalendarItems.AddRange(Incomplete(team, ann, "Dentist"), Incomplete(team, ann, "Gym"));
        await db.SaveChangesAsync();

        var reminded = await Job(db).RunAsync(Morning, CancellationToken.None);

        Assert.Equal(1, reminded);
        var entry = Assert.Single(db.Notifications);
        Assert.Equal(NotificationEvent.IncompleteItemsReminder, entry.Event);
        Assert.Equal("2 items need your details", entry.Title);
        Assert.Contains("\"Dentist\"", entry.Body);
        Assert.Contains("\"Gym\"", entry.Body);
        Assert.Null(entry.TaskId); // several items: tapping only marks it read
        Assert.Equal(PushDeliveryState.Pending, entry.PushState);
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Run_ASingleItem_OpensDirectlyWhenTapped()
    {
        var (db, ann, team) = await SetUpAsync();
        var item = Incomplete(team, ann, "Dentist");
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();

        await Job(db).RunAsync(Morning, CancellationToken.None);

        var entry = Assert.Single(db.Notifications);
        Assert.Equal("1 item needs your details", entry.Title);
        Assert.Equal(item.Id, entry.TaskId);
        Assert.Equal(team.Id, entry.TeamId);
    }

    [Fact]
    public async Task Run_NothingIncomplete_SendsNothing()
    {
        var (db, ann, team) = await SetUpAsync();
        var complete = Incomplete(team, ann, "Fine");
        complete.IsDataIncomplete = false;
        db.CalendarItems.Add(complete);
        await db.SaveChangesAsync();

        var reminded = await Job(db).RunAsync(Morning, CancellationToken.None);

        Assert.Equal(0, reminded);
        Assert.Empty(db.Notifications);
        Assert.Empty(db.NotificationPreferences); // not even a settings row for nothing
    }

    [Fact]
    public async Task Run_SkipsFinishedOverAndUnrankedItems()
    {
        var (db, ann, team) = await SetUpAsync();
        var done = Incomplete(team, ann, "Done task", TaskType.Task);
        done.Status = TaskStatus.Completed;
        var cancelled = Incomplete(team, ann, "Cancelled event");
        cancelled.Status = TaskStatus.Cancelled;
        var over = Incomplete(team, ann, "Over", endUtc: Morning.AddHours(-1));
        var birthday = Incomplete(team, ann, "Mum's birthday");
        birthday.EventTemplate = EventTemplate.Birthday; // Informational profile: never ranked, so never "incomplete"
        var unassigned = Incomplete(team, ann, "Nobody's");
        unassigned.AssignedUserId = null;
        var counted = Incomplete(team, ann, "Counted");
        db.CalendarItems.AddRange(done, cancelled, over, birthday, unassigned, counted);
        await db.SaveChangesAsync();

        await Job(db).RunAsync(Morning, CancellationToken.None);

        var entry = Assert.Single(db.Notifications);
        Assert.Equal("1 item needs your details", entry.Title);
        Assert.Contains("Counted", entry.Body);
    }

    [Theory]
    [InlineData(6, false)]   // 06:00 - too early
    [InlineData(9, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]  // 21:00 - too late
    public async Task Run_OnlySendsInsideTheMorningToEveningWindow(int utcHour, bool expected)
    {
        var (db, ann, team) = await SetUpAsync();
        db.CalendarItems.Add(Incomplete(team, ann, "Dentist"));
        await db.SaveChangesAsync();

        var reminded = await Job(db).RunAsync(new DateTime(2026, 10, 20, utcHour, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        Assert.Equal(expected ? 1 : 0, reminded);
    }

    [Fact]
    public async Task Run_UsesTheUsersOwnTimeZone()
    {
        var (db, ann, team) = await SetUpAsync();
        ann.TimeZoneId = "Europe/Warsaw"; // UTC+2: 07:30 UTC is 09:30 there
        db.CalendarItems.Add(Incomplete(team, ann, "Dentist"));
        await db.SaveChangesAsync();
        var job = Job(db);

        Assert.Equal(0, await job.RunAsync(new DateTime(2026, 10, 20, 6, 30, 0, DateTimeKind.Utc), CancellationToken.None)); // 08:30 local
        Assert.Equal(1, await job.RunAsync(new DateTime(2026, 10, 20, 7, 30, 0, DateTimeKind.Utc), CancellationToken.None)); // 09:30 local
    }

    [Fact]
    public async Task Run_AnUnknownTimeZone_FallsBackToUtcInsteadOfSkippingTheUser()
    {
        var (db, ann, team) = await SetUpAsync();
        ann.TimeZoneId = "Mars/Olympus_Mons";
        db.CalendarItems.Add(Incomplete(team, ann, "Dentist"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await Job(db).RunAsync(Morning, CancellationToken.None));
    }

    [Fact]
    public async Task Run_AtMostOncePerLocalDay_EvenWhenTheEntryWasDeleted_ThenAgainTomorrow()
    {
        var (db, ann, team) = await SetUpAsync();
        db.CalendarItems.Add(Incomplete(team, ann, "Dentist"));
        await db.SaveChangesAsync();
        var job = Job(db);

        Assert.Equal(1, await job.RunAsync(Morning, CancellationToken.None));
        Assert.Equal(0, await job.RunAsync(Morning.AddHours(3), CancellationToken.None)); // same day

        db.Notifications.RemoveRange(db.Notifications); // the user cleared it
        await db.SaveChangesAsync();
        Assert.Equal(0, await job.RunAsync(Morning.AddHours(4), CancellationToken.None)); // still the same day: not re-created

        Assert.Equal(1, await job.RunAsync(Morning.AddDays(1), CancellationToken.None)); // next morning
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task Run_NextDay_RefreshesTheSameEntry_UnreadOnTopAndPushedAgain()
    {
        var (db, ann, team) = await SetUpAsync();
        db.CalendarItems.Add(Incomplete(team, ann, "Dentist"));
        await db.SaveChangesAsync();
        var job = Job(db);
        await job.RunAsync(Morning, CancellationToken.None);

        var entry = db.Notifications.Single();
        entry.ReadAtUtc = Morning.AddHours(1);
        entry.PushState = PushDeliveryState.Sent;
        db.CalendarItems.Add(Incomplete(team, ann, "Gym"));
        await db.SaveChangesAsync();

        await job.RunAsync(Morning.AddDays(1), CancellationToken.None);

        var refreshed = Assert.Single(db.Notifications); // still one row, not one per day
        Assert.Equal(entry.Id, refreshed.Id);
        Assert.Equal("2 items need your details", refreshed.Title);
        Assert.Null(refreshed.ReadAtUtc);
        Assert.Equal(Morning.AddDays(1), refreshed.CreatedAtUtc);
        Assert.Equal(PushDeliveryState.Pending, refreshed.PushState);
        Assert.Equal(0, refreshed.PushAttempts);
    }

    [Fact]
    public async Task Run_SwitchedOff_SendsNothing()
    {
        var (db, ann, team) = await SetUpAsync();
        db.NotificationPreferences.Add(new NotificationPreference { UserId = ann.Id, IncompleteItemsReminder = false });
        db.CalendarItems.Add(Incomplete(team, ann, "Dentist"));
        await db.SaveChangesAsync();

        Assert.Equal(0, await Job(db).RunAsync(Morning, CancellationToken.None));
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Run_PushSwitchedOff_StillFillsTheInboxButAsksForNoPush()
    {
        var (db, ann, team) = await SetUpAsync();
        db.NotificationPreferences.Add(new NotificationPreference { UserId = ann.Id, PushEnabled = false });
        db.CalendarItems.Add(Incomplete(team, ann, "Dentist"));
        await db.SaveChangesAsync();

        await Job(db).RunAsync(Morning, CancellationToken.None);

        Assert.Equal(PushDeliveryState.NotWanted, Assert.Single(db.Notifications).PushState);
    }

    [Fact]
    public async Task Run_LeavesOutMutedTeams()
    {
        var (db, ann, team) = await SetUpAsync();
        var mutedTeam = await TestDb.AddTeamAsync(db, (ann, TeamRole.Member));
        db.TeamMembers.Single(m => m.TeamId == mutedTeam.Id).MuteNotifications = true;
        db.CalendarItems.AddRange(Incomplete(mutedTeam, ann, "Muted one"), Incomplete(team, ann, "Audible one"));
        await db.SaveChangesAsync();

        await Job(db).RunAsync(Morning, CancellationToken.None);

        var entry = Assert.Single(db.Notifications);
        Assert.Contains("Audible one", entry.Body);
        Assert.DoesNotContain("Muted one", entry.Body);
    }

    [Fact]
    public async Task Run_EachUserGetsTheirOwnReminder()
    {
        var (db, ann, team) = await SetUpAsync();
        var bob = await TestDb.AddUserAsync(db, "Bob");
        db.CalendarItems.AddRange(Incomplete(team, ann, "Ann's"), Incomplete(team, bob, "Bob's"));
        await db.SaveChangesAsync();

        var reminded = await Job(db).RunAsync(Morning, CancellationToken.None);

        Assert.Equal(2, reminded);
        Assert.Contains("Ann's", db.Notifications.Single(n => n.UserId == ann.Id).Body);
        Assert.Contains("Bob's", db.Notifications.Single(n => n.UserId == bob.Id).Body);
    }
}
