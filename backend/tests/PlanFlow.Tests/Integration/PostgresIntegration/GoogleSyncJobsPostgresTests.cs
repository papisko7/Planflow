using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.ExternalServices.Google;
using PlanFlow.Infrastructure.Persistence;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The two Google sync jobs on a real PostgreSQL database, fed by the real Google clients over a stub transport (real JSON,
/// real retry pipeline). The unit tests of the jobs run on InMemory, which does not enforce column widths, unique indexes or foreign-key
/// behaviour, and translates queries its own way; these tests cover exactly that. Every run is a "Sync now" press for one account
/// (the user id travels in the job data), which also keeps the tests apart on the shared database.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GoogleSyncJobsPostgresTests
{
    private readonly PostgresWebApplicationFactory _factory;

    public GoogleSyncJobsPostgresTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Account(Guid UserId, Guid TeamId, Guid IntegrationId);

    private async Task<Account> SeedAccountAsync(DateTime? lastSyncedAtUtc = null, DateTime? lastFullSyncAtUtc = null, string scopes = CalendarIntegration.TasksScope)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Syncer", PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var team = new Team { Name = "Personal", IsPersonal = true };
        var integration = new CalendarIntegration
        {
            UserId = user.Id,
            Provider = CalendarProvider.Google,
            ExternalAccountId = "sub-" + user.Id,
            ExternalAccountEmail = user.Email,
            EncryptedAccessToken = "access-token",
            EncryptedRefreshToken = "refresh-token",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            GrantedScopes = scopes,
            LastSyncedAtUtc = lastSyncedAtUtc,
            LastFullSyncAtUtc = lastFullSyncAtUtc,
            IsActive = true
        };
        db.Users.Add(user);
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { UserId = user.Id, TeamId = team.Id, Role = TeamRole.Owner });
        db.CalendarIntegrations.Add(integration);
        await db.SaveChangesAsync();
        return new Account(user.Id, team.Id, integration.Id);
    }

    private static Mock<IJobExecutionContext> PressOf(Guid userId)
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.MergedJobDataMap).Returns(new JobDataMap { ["UserId"] = userId.ToString() });
        return context;
    }

    private static GoogleAccessTokenProvider TokenProvider(PlanFlowDbContext db)
    {
        var encryption = new Mock<ITokenEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
        encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
        return new GoogleAccessTokenProvider(Mock.Of<IGoogleOAuthClient>(), encryption.Object, db);
    }

    private async Task PressCalendarAsync(Account account, StubHttpMessageHandler google)
    {
        var services = new ServiceCollection();
        services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>().AddGoogleCalendarRetry().ConfigurePrimaryHttpMessageHandler(() => google);
        await using var provider = services.BuildServiceProvider();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var job = new SyncCalendarJob(
            db, TokenProvider(db), provider.GetRequiredService<IGoogleCalendarClient>(), scope.ServiceProvider.GetRequiredService<ICacheService>(),
            new NotificationService(db), NullLogger<SyncCalendarJob>.Instance);
        await job.Execute(PressOf(account.UserId).Object);
    }

    private async Task PressTasksAsync(Account account, StubHttpMessageHandler google)
    {
        var services = new ServiceCollection();
        services.AddHttpClient<IGoogleTasksClient, GoogleTasksClient>().AddGoogleCalendarRetry().ConfigurePrimaryHttpMessageHandler(() => google);
        await using var provider = services.BuildServiceProvider();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var job = new SyncTasksJob(
            db, TokenProvider(db), provider.GetRequiredService<IGoogleTasksClient>(), scope.ServiceProvider.GetRequiredService<ICacheService>(),
            new NotificationService(db), NullLogger<SyncTasksJob>.Instance);
        await job.Execute(PressOf(account.UserId).Object);
    }

    private async Task<T> ReadAsync<T>(Func<PlanFlowDbContext, Task<T>> read)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>());
    }

    private async Task WriteAsync(Func<PlanFlowDbContext, Task> change)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        await change(db);
        await db.SaveChangesAsync();
    }

    private static StubHttpMessageHandler Calendar(params string[] events) => new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, GoogleJson.Events(events));

    /// <summary>Whole seconds, so a value survives the trip through JSON and PostgreSQL's microsecond clock unchanged.</summary>
    private static DateTime InDays(double days)
    {
        var moment = DateTime.UtcNow.AddDays(days);
        return new DateTime(moment.Ticks - moment.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    private static DateTime Day(int daysFromToday) => AllDayTime.LiteralToday(DateTime.UtcNow, TimeZoneInfo.Utc).AddDays(daysFromToday);

    // --- SyncCalendarJob -------------------------------------------------------------------

    [Fact]
    public async Task Calendar_FirstPress_ImportsEventsWithTheTag_FlagsOnlyTheOneWithoutData_AndNotifiesOnce()
    {
        var account = await SeedAccountAsync();
        var soon = InDays(2);

        await PressCalendarAsync(account, Calendar(
            GoogleJson.Event("tagged", "Thesis defense prep", soon, soon.AddHours(1), description: "Rehearse<br>[planflow] impact=8 effort=90min",
                location: "Room 4", colorId: "5", reminderMinutes: [30]),
            GoogleJson.Event("plain", "Dentist", soon.AddHours(5), soon.AddHours(6))));

        var items = await ReadAsync(db => db.CalendarItems.Include(i => i.Reminders).AsNoTracking().Where(i => i.TeamId == account.TeamId).ToListAsync());
        Assert.Equal(2, items.Count);

        var tagged = items.Single(i => i.ExternalCalendarEventId == "tagged");
        Assert.Equal(TaskType.Event, tagged.TaskType);
        Assert.Equal(soon, tagged.StartUtc);
        Assert.Equal("Rehearse", tagged.Description); // the [planflow] line fills the fields, it is not kept in the text
        Assert.Equal(8, tagged.ImpactScore);
        Assert.Equal(1.5, tagged.EstimatedEffortHours);
        Assert.Equal("Room 4", tagged.Location);
        Assert.Equal("banana", tagged.ColorKey);
        Assert.False(tagged.IsDataIncomplete);
        Assert.Equal(30, tagged.Reminders.Single().OffsetMinutes);
        Assert.True(tagged.CurrentUrgencyScore > 0); // scored right away, through the translated team query

        var plain = items.Single(i => i.ExternalCalendarEventId == "plain");
        Assert.Null(plain.ImpactScore);
        Assert.True(plain.IsDataIncomplete);

        var notifications = await ReadAsync(db => db.Notifications.AsNoTracking()
            .Where(n => n.UserId == account.UserId && n.Event == NotificationEvent.CalendarItemsIncomplete).ToListAsync());
        var message = Assert.Single(notifications);
        Assert.Contains("Dentist", message.Title + message.Body);
        Assert.DoesNotContain("Thesis defense prep", message.Title + message.Body);

        var integration = await ReadAsync(db => db.CalendarIntegrations.AsNoTracking().SingleAsync(i => i.Id == account.IntegrationId));
        Assert.NotNull(integration.LastSyncedAtUtc);
        Assert.NotNull(integration.LastFullSyncAtUtc);
    }

    [Fact]
    public async Task Calendar_PressAgain_NeverDuplicates_AndTheTagOverwritesTheUsersEditOnlyWhenItsTextChanged()
    {
        var account = await SeedAccountAsync();
        var soon = InDays(3);
        StubHttpMessageHandler Google(string tag) => Calendar(
            GoogleJson.Event("e1", "Review", soon, soon.AddHours(1), description: $"[planflow] impact={tag} effort=1h"));

        await PressCalendarAsync(account, Google("8"));
        await WriteAsync(async db => (await db.CalendarItems.SingleAsync(i => i.TeamId == account.TeamId)).ImpactScore = 3); // the user changed it in the app

        await PressCalendarAsync(account, Google("8")); // Google's text is the same as last time: the user's edit stays
        var item = await ReadAsync(db => db.CalendarItems.AsNoTracking().SingleAsync(i => i.TeamId == account.TeamId));
        Assert.Equal(3, item.ImpactScore);

        await PressCalendarAsync(account, Google("9")); // the tag was edited in Google: now it wins
        Assert.Equal(1, await ReadAsync(db => db.CalendarItems.CountAsync(i => i.TeamId == account.TeamId)));
        item = await ReadAsync(db => db.CalendarItems.AsNoTracking().SingleAsync(i => i.TeamId == account.TeamId));
        Assert.Equal(9, item.ImpactScore);
    }

    [Fact]
    public async Task Calendar_EventCancelledInGoogle_IsMarkedCancelledNotErased()
    {
        var account = await SeedAccountAsync();
        var soon = InDays(2);

        await PressCalendarAsync(account, Calendar(GoogleJson.Event("e1", "Offsite", soon, soon.AddHours(2))));
        await PressCalendarAsync(account, Calendar(GoogleJson.Event("e1", "Offsite", soon, soon.AddHours(2), status: "cancelled")));

        var item = await ReadAsync(db => db.CalendarItems.AsNoTracking().SingleAsync(i => i.TeamId == account.TeamId));
        Assert.Equal(TaskStatus.Cancelled, item.Status);
        Assert.Equal("cancelled", item.ExternalStatus);
    }

    [Fact]
    public async Task Calendar_TitleAndPlaceLongerThanTheirColumns_AreCutInsteadOfFailingTheWholeSync()
    {
        var account = await SeedAccountAsync();
        var soon = InDays(2);

        // PostgreSQL rejects an over-long value (varchar(256) / varchar(512)); InMemory would store it silently.
        await PressCalendarAsync(account, Calendar(
            GoogleJson.Event("long", new string('T', 300), soon, soon.AddHours(1), location: new string('L', 600)),
            GoogleJson.Event("short", "Short one", soon.AddHours(2), soon.AddHours(3))));

        var items = await ReadAsync(db => db.CalendarItems.AsNoTracking().Where(i => i.TeamId == account.TeamId).ToListAsync());
        Assert.Equal(2, items.Count);
        var cut = items.Single(i => i.ExternalCalendarEventId == "long");
        Assert.Equal(256, cut.Title.Length);
        Assert.Equal(512, cut.Location!.Length);
    }

    [Fact]
    public async Task Calendar_FullPass_RemovesImportedItemsBeyondTheWindowWithTheirReminders_AndKeepsTheRest()
    {
        var twoDaysAgo = DateTime.UtcNow.AddDays(-2);
        var account = await SeedAccountAsync(lastSyncedAtUtc: twoDaysAgo, lastFullSyncAtUtc: twoDaysAgo); // the next press is a full pass
        var now = DateTime.UtcNow;

        CalendarItem Imported(string externalId, TaskType type, DateTime? start, DateTime? deadline) => new()
        {
            TeamId = account.TeamId, AssignedUserId = account.UserId, Title = externalId, TaskType = type,
            Status = type == TaskType.Event ? TaskStatus.Confirmed : TaskStatus.Backlog, StartUtc = start, EndUtc = start?.AddHours(1), DeadlineUtc = deadline,
            SourceCalendarIntegrationId = account.IntegrationId, ExternalCalendarEventId = externalId
        };
        var far = Imported("far", TaskType.Event, now.AddDays(200), null);
        far.Reminders.Add(ItemReminder.Offset(30));
        var oldTask = Imported("old-task", TaskType.Task, null, now.AddDays(300)); // an older row: a Task with a deadline, no start
        var near = Imported("near", TaskType.Event, now.AddDays(10), null);
        var manual = new CalendarItem { TeamId = account.TeamId, Title = "Mine, far away", TaskType = TaskType.Event, Status = TaskStatus.Confirmed, StartUtc = now.AddDays(200), EndUtc = now.AddDays(200).AddHours(1) };
        await WriteAsync(db =>
        {
            db.CalendarItems.AddRange(far, oldTask, near, manual);
            return Task.CompletedTask;
        });

        await PressCalendarAsync(account, new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, GoogleJson.Events()));

        var titles = await ReadAsync(db => db.CalendarItems.AsNoTracking().Where(i => i.TeamId == account.TeamId).Select(i => i.Title).ToListAsync());
        Assert.Equivalent(new[] { "near", "Mine, far away" }, titles);
        Assert.Equal(0, await ReadAsync(db => db.ItemReminders.CountAsync(r => r.CalendarItemId == far.Id))); // gone with the item (cascade)
    }

    [Fact]
    public async Task Calendar_PressByOneUser_LeavesTheOtherAccountAlone()
    {
        var pressing = await SeedAccountAsync();
        var other = await SeedAccountAsync();
        var soon = InDays(2);
        var google = Calendar(GoogleJson.Event("e1", "Mine", soon, soon.AddHours(1)));

        await PressCalendarAsync(pressing, google);

        Assert.Single(google.Requests); // Google was asked once, for one account
        Assert.Equal(1, await ReadAsync(db => db.CalendarItems.CountAsync(i => i.TeamId == pressing.TeamId)));
        Assert.Equal(0, await ReadAsync(db => db.CalendarItems.CountAsync(i => i.TeamId == other.TeamId)));
        Assert.Null((await ReadAsync(db => db.CalendarIntegrations.AsNoTracking().SingleAsync(i => i.Id == other.IntegrationId))).LastSyncedAtUtc);
    }

    // --- SyncTasksJob ----------------------------------------------------------------------

    private static StubHttpMessageHandler TasksOf(params (string ListId, string ListTitle, string[] Tasks)[] lists)
    {
        var google = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, GoogleJson.TaskLists(lists.Select(l => (l.ListId, l.ListTitle)).ToArray()));
        foreach (var list in lists)
        {
            google.Enqueue(HttpStatusCode.OK, GoogleJson.Tasks(list.Tasks));
        }
        return google;
    }

    [Fact]
    public async Task Tasks_FirstPress_ImportsOnlyOpenTopLevelTasks_DueDayBecomesTheDeadline_StartIsTheFirstSeenDay()
    {
        var account = await SeedAccountAsync();

        await PressTasksAsync(account, TasksOf(("l1", "My Tasks", [
            GoogleJson.Task("t1", "Write chapter 3", due: Day(5), notes: "Check the sources"),
            GoogleJson.Task("t2", "Already done", due: Day(1), status: "completed"),
            GoogleJson.Task("t3", "A sub-task", parent: "t1"),
            GoogleJson.Task("t4", "Someday")])));

        var items = await ReadAsync(db => db.CalendarItems.AsNoTracking().Where(i => i.TeamId == account.TeamId).ToListAsync());
        Assert.Equivalent(new[] { "Write chapter 3", "Someday" }, items.Select(i => i.Title));

        var chapter = items.Single(i => i.ExternalTaskId == "t1");
        Assert.Equal(TaskType.Task, chapter.TaskType);
        Assert.Equal("Check the sources", chapter.Description);
        Assert.Equal(Day(5), chapter.DeadlineUtc);
        Assert.Equal(Day(0), chapter.StartUtc);
        Assert.True(chapter.IsAllDay);
        Assert.True(chapter.IsDataIncomplete); // always unverified until the user saves it
        Assert.False(chapter.IsDeadlineDefaulted);

        var someday = items.Single(i => i.ExternalTaskId == "t4");
        Assert.True(someday.IsDeadlineDefaulted); // no due day: a placeholder of "tomorrow"
        Assert.Equal(Day(1), someday.DeadlineUtc);

        var list = await ReadAsync(db => db.ExternalTaskLists.AsNoTracking().SingleAsync(l => l.CalendarIntegrationId == account.IntegrationId));
        Assert.Equal("My Tasks", list.Title);
        Assert.All(items, i => Assert.Equal(list.Id, i.ExternalTaskListId));

        var notifications = await ReadAsync(db => db.Notifications.AsNoTracking()
            .Where(n => n.UserId == account.UserId && n.Event == NotificationEvent.CalendarItemsIncomplete).ToListAsync());
        Assert.Single(notifications);
        Assert.Equal(2, await ReadAsync(db => db.UrgencyScoreLogs.CountAsync(l => l.CalendarItem.TeamId == account.TeamId && l.TriggerSource == ScoreTriggerSource.CalendarSync)));
    }

    [Fact]
    public async Task Tasks_PressAgain_UpdatesTheSameRow_AndNeverMovesTheStart()
    {
        var account = await SeedAccountAsync();

        await PressTasksAsync(account, TasksOf(("l1", "My Tasks", [GoogleJson.Task("t1", "Draft", due: Day(7))])));
        var first = await ReadAsync(db => db.CalendarItems.AsNoTracking().SingleAsync(i => i.TeamId == account.TeamId));

        await PressTasksAsync(account, TasksOf(("l1", "My Tasks", [GoogleJson.Task("t1", "Draft, second version", due: Day(9))])));

        var second = await ReadAsync(db => db.CalendarItems.AsNoTracking().SingleAsync(i => i.TeamId == account.TeamId));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Draft, second version", second.Title);
        Assert.Equal(Day(9), second.DeadlineUtc);
        Assert.Equal(first.StartUtc, second.StartUtc);
    }

    [Fact]
    public async Task Tasks_ListRemovedInGoogle_CancelsItsOpenTasks_AndTheListRowGoes()
    {
        var account = await SeedAccountAsync();
        await PressTasksAsync(account, TasksOf(
            ("l1", "Old list", [GoogleJson.Task("a", "In the old list", due: Day(3))]),
            ("l2", "Kept list", [GoogleJson.Task("b", "In the kept list", due: Day(3))])));

        await PressTasksAsync(account, TasksOf(("l2", "Kept list", [GoogleJson.Task("b", "In the kept list", due: Day(3))])));

        var items = await ReadAsync(db => db.CalendarItems.AsNoTracking().Where(i => i.TeamId == account.TeamId).ToListAsync());
        var orphan = items.Single(i => i.ExternalTaskId == "a");
        Assert.Equal(TaskStatus.Cancelled, orphan.Status);
        Assert.Null(orphan.ExternalTaskListId);
        Assert.Equal(TaskStatus.Backlog, items.Single(i => i.ExternalTaskId == "b").Status);
        var lists = await ReadAsync(db => db.ExternalTaskLists.AsNoTracking().Where(l => l.CalendarIntegrationId == account.IntegrationId).Select(l => l.ExternalId).ToListAsync());
        Assert.Equal(["l2"], lists);
    }

    // --- Edits made in the app travel back to Google ---------------------------------------

    private const string WriteScopes = "openid email " + CalendarIntegration.CalendarWriteScope + " " + CalendarIntegration.TasksWriteScope;

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Calendar_EditMadeInTheApp_IsReadAndPatchedInGoogle_AndNothingWaitsAfterwards()
    {
        var account = await SeedAccountAsync(scopes: WriteScopes);
        var soon = InDays(2);
        await PressCalendarAsync(account, Calendar(GoogleJson.Event("e1", "Standup", soon, soon.AddHours(1), location: "Room 1")));

        // The person edits the title and the place in the app; the edit command notes it exactly like this.
        await WriteAsync(async db =>
        {
            var item = await db.CalendarItems.Include(i => i.Reminders).SingleAsync(i => i.TeamId == account.TeamId);
            var before = GoogleMirror.Capture(item);
            item.Title = "Daily";
            item.Location = "Room 7";
            GoogleMirror.Track(item, before, GoogleMirror.Capture(item), DateTime.UtcNow);
        });
        var waiting = await ReadAsync(db => db.CalendarItems.AsNoTracking().SingleAsync(i => i.TeamId == account.TeamId));
        Assert.Equal(MirroredFields.Title | MirroredFields.Location, waiting.LocalEditedFields);

        string? patchBody = null;
        var google = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, GoogleJson.Events())                                                       // nothing changed in Google since the last press
            .Enqueue(HttpStatusCode.OK, GoogleJson.Event("e1", "Standup", soon, soon.AddHours(1), location: "Room 1")) // the event as Google holds it
            .Enqueue(request =>
            {
                patchBody = request.Content!.ReadAsStringAsync().Result;
                return Json(GoogleJson.Event("e1", "Daily", soon, soon.AddHours(1), location: "Room 7"));
            });
        await PressCalendarAsync(account, google);

        Assert.Equal(3, google.Requests.Count);
        Assert.Equal(HttpMethod.Get, google.Requests[1].Method);
        Assert.EndsWith("/events/e1", google.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(new HttpMethod("PATCH"), google.Requests[2].Method);
        Assert.Contains("sendUpdates=none", google.Requests[2].RequestUri!.Query);
        var sent = JsonNode.Parse(patchBody!)!;
        Assert.Equal("Daily", (string?)sent["summary"]);
        Assert.Equal("Room 7", (string?)sent["location"]);

        var item = await ReadAsync(db => db.CalendarItems.AsNoTracking().SingleAsync(i => i.TeamId == account.TeamId));
        Assert.Equal("Daily", item.Title);
        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
        Assert.Null(item.LocalEditedAtUtc);
    }

    [Fact]
    public async Task Tasks_TaskMarkedDoneInTheApp_IsClosedInGoogle()
    {
        var account = await SeedAccountAsync(scopes: WriteScopes);
        await PressTasksAsync(account, TasksOf(("l1", "My Tasks", [GoogleJson.Task("t1", "Draft", due: Day(7))])));
        await WriteAsync(async db =>
        {
            var item = await db.CalendarItems.Include(i => i.Reminders).SingleAsync(i => i.TeamId == account.TeamId);
            var before = GoogleMirror.Capture(item);
            item.Status = TaskStatus.Completed;
            GoogleMirror.Track(item, before, GoogleMirror.Capture(item), DateTime.UtcNow);
        });

        string? patchBody = null;
        var google = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, GoogleJson.TaskLists(("l1", "My Tasks")))
            .Enqueue(HttpStatusCode.OK, GoogleJson.Tasks())                                         // nothing changed in Google since the last press
            .Enqueue(HttpStatusCode.OK, GoogleJson.Task("t1", "Draft", due: Day(7)))                // the task as Google holds it
            .Enqueue(request =>
            {
                patchBody = request.Content!.ReadAsStringAsync().Result;
                return Json(GoogleJson.Task("t1", "Draft", due: Day(7), status: "completed"));
            });
        await PressTasksAsync(account, google);

        Assert.Equal(4, google.Requests.Count);
        Assert.Equal(new HttpMethod("PATCH"), google.Requests[3].Method);
        Assert.EndsWith("/lists/l1/tasks/t1", google.Requests[3].RequestUri!.AbsolutePath);
        Assert.Equal("completed", (string?)JsonNode.Parse(patchBody!)!["status"]);

        var item = await ReadAsync(db => db.CalendarItems.AsNoTracking().SingleAsync(i => i.TeamId == account.TeamId));
        Assert.Equal(TaskStatus.Completed, item.Status);
        Assert.Equal("completed", item.ExternalStatus);
        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
    }

    // --- The database itself ---------------------------------------------------------------

    [Fact]
    public async Task TheDatabaseRefusesASecondRowForTheSameGoogleEventOrTask()
    {
        var account = await SeedAccountAsync();
        CalendarItem Row(string? eventId, string? taskId) => new()
        {
            TeamId = account.TeamId, Title = "Twin", TaskType = TaskType.Task, Status = TaskStatus.Backlog, DeadlineUtc = DateTime.UtcNow.AddDays(1),
            SourceCalendarIntegrationId = account.IntegrationId, ExternalCalendarEventId = eventId, ExternalTaskId = taskId
        };

        foreach (var (eventId, taskId) in new (string?, string?)[] { ("same-event", null), (null, "same-task") })
        {
            await WriteAsync(db =>
            {
                db.CalendarItems.Add(Row(eventId, taskId));
                return Task.CompletedTask;
            });

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => WriteAsync(db =>
            {
                db.CalendarItems.Add(Row(eventId, taskId));
                return Task.CompletedTask;
            }));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)failure.InnerException!).SqlState);
        }
    }
}
