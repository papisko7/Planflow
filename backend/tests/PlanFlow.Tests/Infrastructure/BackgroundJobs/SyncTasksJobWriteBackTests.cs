using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using Quartz;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>
/// The Google Tasks sync in both directions: an edit made in the app (title, notes, due day, done / open) reaches Google, and when both
/// sides changed the same task the later change wins (last write wins).
/// </summary>
public class SyncTasksJobWriteBackTests
{
    private const string ReadOnlyScopes = "openid email https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/tasks.readonly";
    private const string WriteScopes = "openid email " + CalendarIntegration.CalendarWriteScope + " " + CalendarIntegration.TasksWriteScope;
    private const string ListId = "l1";

    /// <summary>Google's task list: its own copy of each task (a patch changes it like the real service) and a record of what was asked of it.</summary>
    private sealed class FakeTasks : IGoogleTasksClient
    {
        public List<GoogleTask> Server { get; } = [];
        public HashSet<string> Listed { get; } = [];
        public List<(string TaskId, GoogleTaskPatch Patch)> Patches { get; } = [];
        public List<string> Gets { get; } = [];
        public Dictionary<string, Exception> PatchFailures { get; } = [];

        public Task<IReadOnlyList<GoogleTaskList>> ListTaskListsAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GoogleTaskList>>([new GoogleTaskList(ListId, "My Tasks")]);

        // The first poll of a list reads everything; a later one only what changed since.
        public Task<IReadOnlyList<GoogleTask>> ListTasksAsync(string accessToken, string taskListId, DateTime? updatedSinceUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GoogleTask>>(Server.Where(t => updatedSinceUtc is null || Listed.Contains(t.Id)).ToList());

        public Task<GoogleTask?> GetTaskAsync(string accessToken, string taskListId, string taskId, CancellationToken cancellationToken)
        {
            Gets.Add(taskId);
            return Task.FromResult(Server.FirstOrDefault(t => t.Id == taskId));
        }

        public Task<GoogleTask> PatchTaskAsync(string accessToken, string taskListId, string taskId, GoogleTaskPatch patch, CancellationToken cancellationToken)
        {
            Patches.Add((taskId, patch));
            if (PatchFailures.TryGetValue(taskId, out var failure))
            {
                throw failure;
            }

            var index = Server.FindIndex(t => t.Id == taskId);
            var task = Server[index];
            Server[index] = task with
            {
                Title = patch.Title ?? task.Title,
                Notes = patch.Notes is null ? task.Notes : patch.Notes.Length == 0 ? null : patch.Notes,
                DueDay = patch.DueDay ?? task.DueDay,
                IsCompleted = patch.IsCompleted ?? task.IsCompleted,
                UpdatedUtc = DateTime.UtcNow
            };
            return Task.FromResult(Server[index]);
        }
    }

    private sealed class Harness
    {
        public Harness(string scopes = WriteScopes)
        {
            Context = new PlanFlowDbContext(new DbContextOptionsBuilder<PlanFlowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var user = new User { Email = "student@example.com", DisplayName = "Student", PasswordHash = "hash", EmailVerifiedAtUtc = DateTime.UtcNow };
            var team = new Team { Name = "Personal", IsPersonal = true };
            Context.Users.Add(user);
            Context.Teams.Add(team);
            Context.TeamMembers.Add(new TeamMember { UserId = user.Id, TeamId = team.Id, Role = TeamRole.Owner });
            Integration = new CalendarIntegration
            {
                UserId = user.Id, Provider = CalendarProvider.Google, ExternalAccountId = "google-sub", ExternalAccountEmail = "student@example.com",
                EncryptedAccessToken = "access-token", EncryptedRefreshToken = "refresh-token", AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                GrantedScopes = scopes, IsActive = true
            };
            Context.CalendarIntegrations.Add(Integration);
            Context.SaveChanges();
        }

        public PlanFlowDbContext Context { get; }
        public CalendarIntegration Integration { get; }
        public FakeTasks Google { get; } = new();
        public Mock<IGoogleOAuthClient> OAuth { get; } = new();

        public async Task SyncAsync()
        {
            var encryption = new Mock<ITokenEncryptionService>();
            encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
            encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
            var cache = new Mock<ICacheService>();
            cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var job = new SyncTasksJob(
                Context, new GoogleAccessTokenProvider(OAuth.Object, encryption.Object, Context), Google, cache.Object,
                new PlanFlow.Application.Notifications.NotificationService(Context), Mock.Of<Microsoft.Extensions.Logging.ILogger<SyncTasksJob>>());

            var jobContext = new Mock<IJobExecutionContext>();
            jobContext.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            jobContext.SetupGet(c => c.MergedJobDataMap).Returns(new JobDataMap());
            await job.Execute(jobContext.Object);
        }

        /// <summary>Google holds the tasks and the first sync imports them.</summary>
        public async Task ImportAsync(params GoogleTask[] tasks)
        {
            Google.Server.AddRange(tasks);
            await SyncAsync();
        }

        public CalendarItem Item(string taskId) => Context.CalendarItems.Single(i => i.ExternalTaskId == taskId);

        /// <summary>An edit made in the app at the given moment, noted the way the edit command notes it.</summary>
        public CalendarItem EditInApp(string taskId, DateTime at, Action<CalendarItem> edit)
        {
            var item = Item(taskId);
            var before = GoogleMirror.Capture(item);
            edit(item);
            GoogleMirror.Track(item, before, GoogleMirror.Capture(item), at);
            Context.SaveChanges();
            return item;
        }

        /// <summary>An edit made in Google at the given moment; the next poll lists the task.</summary>
        public void EditInGoogle(string taskId, DateTime at, Func<GoogleTask, GoogleTask> edit)
        {
            var index = Google.Server.FindIndex(t => t.Id == taskId);
            Google.Server[index] = edit(Google.Server[index]) with { UpdatedUtc = at };
            Google.Listed.Add(taskId);
        }
    }

    private static DateTime Day(int daysFromToday) => AllDayTime.LiteralToday(DateTime.UtcNow, TimeZoneInfo.Utc).AddDays(daysFromToday);

    private static GoogleTask Chapter(string id = "t1") =>
        new(id, "Write chapter 3", "Check the sources", Day(5), IsCompleted: false, IsDeleted: false, UpdatedUtc: DateTime.UtcNow.AddDays(-1));

    // ---- an edit in the app reaches Google ----

    [Fact]
    public async Task TaskMarkedDoneInTheApp_IsClosedInGoogle()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => i.Status = TaskStatus.Completed);

        await h.SyncAsync();

        Assert.Equal(["t1"], h.Google.Gets);                   // not listed (unchanged in Google): read first
        Assert.True(Assert.Single(h.Google.Patches).Patch.IsCompleted);
        Assert.True(h.Google.Server[0].IsCompleted);

        var item = h.Item("t1");
        Assert.Equal(TaskStatus.Completed, item.Status);
        Assert.Equal("completed", item.ExternalStatus);        // Google's state as the app now knows it
        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
        Assert.Null(item.LocalEditedAtUtc);
    }

    [Fact]
    public async Task TaskReopenedInTheApp_IsOpenedInGoogleAgain()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddHours(-2), i => i.Status = TaskStatus.Completed);
        await h.SyncAsync();
        Assert.True(h.Google.Server[0].IsCompleted);

        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => i.Status = TaskStatus.Backlog);
        await h.SyncAsync();

        Assert.False(h.Google.Server[0].IsCompleted);
        Assert.False(h.Google.Patches.Last().Patch.IsCompleted);
        Assert.Equal("needsAction", h.Item("t1").ExternalStatus);
    }

    [Fact]
    public async Task TitleNotesAndDueDayEditedInTheApp_AreSent()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i =>
        {
            i.Title = "Write chapter 4";
            i.Description = "Check the new sources";
            i.DeadlineUtc = Day(9);
        });

        await h.SyncAsync();

        var patch = Assert.Single(h.Google.Patches).Patch;
        Assert.Equal("Write chapter 4", patch.Title);
        Assert.Equal("Check the new sources", patch.Notes);
        Assert.Equal(Day(9), patch.DueDay);
        Assert.Null(patch.IsCompleted);
        Assert.Equal(Day(9), h.Google.Server[0].DueDay);
    }

    [Fact]
    public async Task AfterTheEditWasSent_TheNextSyncSeesNoChange_AndSendsNothingAgain()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => { i.Title = "Write chapter 4"; i.Status = TaskStatus.Completed; });
        await h.SyncAsync();
        var fingerprint = h.Item("t1").ExternalFieldsHash;

        h.Google.Listed.Add("t1");           // Google lists the task again: our own change made it "recently updated"
        await h.SyncAsync();

        Assert.Single(h.Google.Patches);
        Assert.Equal(fingerprint, h.Item("t1").ExternalFieldsHash);
        Assert.Equal("Write chapter 4", h.Item("t1").Title);
        Assert.Equal(TaskStatus.Completed, h.Item("t1").Status);
    }

    [Fact]
    public async Task EditInTheApp_ThatGoogleListedUnchanged_IsSentWithoutReadingTheTask()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddHours(-2), i => i.Title = "Write chapter 4");
        h.EditInGoogle("t1", DateTime.UtcNow.AddMinutes(-30), t => t);   // touched later, same content

        await h.SyncAsync();

        Assert.Empty(h.Google.Gets);
        Assert.Equal("Write chapter 4", Assert.Single(h.Google.Patches).Patch.Title);
    }

    [Fact]
    public async Task RenamingATask_KeepsTheImpactAndEffortWrittenInItsNotes()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter() with { Notes = "Check the sources\n[planflow] impact=7 effort=3h" });
        Assert.Equal(7, h.Item("t1").ImpactScore);
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => i.Description = "Check the new sources");

        await h.SyncAsync();

        Assert.Equal("Check the new sources\n[planflow] impact=7 effort=3h", h.Google.Server[0].Notes);
        h.Google.Listed.Add("t1");
        await h.SyncAsync();                                   // reading it back changes nothing
        Assert.Equal("Check the new sources", h.Item("t1").Description);
        Assert.Equal(7, h.Item("t1").ImpactScore);
        Assert.Equal(3, h.Item("t1").EstimatedEffortHours);
    }

    // ---- both sides changed: the later change wins ----

    [Fact]
    public async Task BothChangedTheTitle_TheLaterEditInTheAppWins_AndIsSent()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInGoogle("t1", DateTime.UtcNow.AddHours(-3), t => t with { Title = "Chapter three (Google)" });
        h.EditInApp("t1", DateTime.UtcNow.AddHours(-1), i => i.Title = "Write chapter 4");

        await h.SyncAsync();

        Assert.Equal("Write chapter 4", h.Item("t1").Title);
        Assert.Equal("Write chapter 4", h.Google.Server[0].Title);
    }

    [Fact]
    public async Task BothChangedTheTitle_TheLaterChangeInGoogleWins_AndNothingIsSent()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddHours(-3), i => i.Title = "Write chapter 4");
        h.EditInGoogle("t1", DateTime.UtcNow.AddHours(-1), t => t with { Title = "Chapter three (Google)" });

        await h.SyncAsync();

        Assert.Equal("Chapter three (Google)", h.Item("t1").Title);
        Assert.Empty(h.Google.Patches);
        Assert.Equal(MirroredFields.None, h.Item("t1").LocalEditedFields);
    }

    [Fact]
    public async Task DoneInTheAppEarlier_ButGoogleChangedTheTaskLater_TheTaskIsOpenAgainHere()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddHours(-3), i => i.Status = TaskStatus.Completed);
        h.EditInGoogle("t1", DateTime.UtcNow.AddHours(-1), t => t with { Title = "Chapter three (Google)" });

        await h.SyncAsync();

        var item = h.Item("t1");
        Assert.Equal("Chapter three (Google)", item.Title);
        Assert.Equal(TaskStatus.Backlog, item.Status);          // Google's version is the later one: still open there
        Assert.Empty(h.Google.Patches);
        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
    }

    [Fact]
    public async Task DoneInTheAppLater_WhileGoogleOnlyChangedTheNotes_BothSurvive()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInGoogle("t1", DateTime.UtcNow.AddHours(-3), t => t with { Notes = "New notes from Google" });
        h.EditInApp("t1", DateTime.UtcNow.AddHours(-1), i => i.Status = TaskStatus.Completed);

        await h.SyncAsync();

        var item = h.Item("t1");
        Assert.Equal("New notes from Google", item.Description);   // what only Google changed is taken over
        Assert.Equal(TaskStatus.Completed, item.Status);           // the later edit stays...
        Assert.True(h.Google.Server[0].IsCompleted);               // ...and is sent
        Assert.Equal("New notes from Google", h.Google.Server[0].Notes);
    }

    [Fact]
    public async Task DoneInGoogleWhileTheTitleWasEditedLaterInTheApp_TheTaskIsDoneAndTheTitleSent()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInGoogle("t1", DateTime.UtcNow.AddHours(-3), t => t with { IsCompleted = true });
        h.EditInApp("t1", DateTime.UtcNow.AddHours(-1), i => i.Title = "Write chapter 4");

        await h.SyncAsync();

        var item = h.Item("t1");
        Assert.Equal(TaskStatus.Completed, item.Status);
        Assert.Equal("Write chapter 4", item.Title);
        Assert.Equal("Write chapter 4", h.Google.Server[0].Title);
        Assert.True(h.Google.Server[0].IsCompleted);
    }

    [Fact]
    public async Task TaskDeletedInGoogle_WinsOverAnEditWaitingInTheApp()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Write chapter 4");
        h.EditInGoogle("t1", DateTime.UtcNow.AddMinutes(-60), t => t with { IsDeleted = true });

        await h.SyncAsync();

        var item = h.Item("t1");
        Assert.Equal(TaskStatus.Cancelled, item.Status);
        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
        Assert.Empty(h.Google.Patches);
    }

    [Fact]
    public async Task TaskAlreadyGoneWhenTheEditIsSent_IsCancelledHere()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Write chapter 4");
        h.Google.Server.Clear();

        await h.SyncAsync();

        Assert.Equal(TaskStatus.Cancelled, h.Item("t1").Status);
        Assert.Equal(MirroredFields.None, h.Item("t1").LocalEditedFields);
    }

    // ---- when Google does not take the edit ----

    [Fact]
    public async Task WithoutThePermissionToChangeTasks_TheEditWaits_AndGoesOutOnceTheUserReconnects()
    {
        var h = new Harness(ReadOnlyScopes);
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => i.Status = TaskStatus.Completed);

        await h.SyncAsync();

        Assert.Empty(h.Google.Gets);
        Assert.Empty(h.Google.Patches);
        Assert.Equal(MirroredFields.Completed, h.Item("t1").LocalEditedFields);   // still waiting
        Assert.Equal(TaskStatus.Completed, h.Item("t1").Status);                  // and still the user's version here

        h.Integration.GrantedScopes = WriteScopes;                                // reconnected
        await h.Context.SaveChangesAsync();
        await h.SyncAsync();

        Assert.True(h.Google.Server[0].IsCompleted);
        Assert.Equal(MirroredFields.None, h.Item("t1").LocalEditedFields);
    }

    [Fact]
    public async Task GoogleRefusesOneTask_ItsEditStaysInTheAppOnly_AndTheOtherTasksAreStillSent()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter(), Chapter("t2"));
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "One");
        h.EditInApp("t2", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Two");
        h.Google.PatchFailures["t1"] = new GoogleWriteRejectedException(GoogleWriteFailure.Rejected, "refused");

        await h.SyncAsync();

        Assert.Equal("Two", h.Google.Server.Single(t => t.Id == "t2").Title);
        Assert.Equal("One", h.Item("t1").Title);
        Assert.Equal(MirroredFields.None, h.Item("t1").LocalEditedFields);
        Assert.Equal(MirroredFields.None, h.Item("t2").LocalEditedFields);
    }

    [Fact]
    public async Task PermissionRefusedByGoogle_StopsSending_AndEveryEditKeepsWaiting()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter(), Chapter("t2"));
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "One");
        h.EditInApp("t2", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Two");
        h.Google.PatchFailures["t1"] = new GoogleWriteRejectedException(GoogleWriteFailure.MissingPermission, "scopes");
        h.Google.PatchFailures["t2"] = new GoogleWriteRejectedException(GoogleWriteFailure.MissingPermission, "scopes");

        await h.SyncAsync();

        Assert.Single(h.Google.Patches);
        Assert.Equal(MirroredFields.Title, h.Item("t1").LocalEditedFields);
        Assert.Equal(MirroredFields.Title, h.Item("t2").LocalEditedFields);
    }

    [Fact]
    public async Task TemporaryFailure_LeavesTheEditWaiting_AndTheNextSyncSendsIt()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i => i.Title = "Write chapter 4");
        h.Google.PatchFailures["t1"] = new HttpRequestException("Google is down");

        await h.SyncAsync();
        Assert.Equal(MirroredFields.Title, h.Item("t1").LocalEditedFields);

        h.Google.PatchFailures.Clear();
        await h.SyncAsync();

        Assert.Equal("Write chapter 4", h.Google.Server[0].Title);
        Assert.Equal(MirroredFields.None, h.Item("t1").LocalEditedFields);
    }

    // ---- what is not sent ----

    [Fact]
    public async Task PlannedStartImpactEffortAndInProgress_AreNotSent_BecauseGoogleTasksHasNoSuchFields()
    {
        var h = new Harness();
        await h.ImportAsync(Chapter());
        h.EditInApp("t1", DateTime.UtcNow.AddMinutes(-5), i =>
        {
            i.ImpactScore = 9;
            i.EstimatedEffortHours = 3;
            i.StartUtc = Day(1);
            i.Status = TaskStatus.InProgress;
        });

        await h.SyncAsync();

        Assert.Empty(h.Google.Patches);
        Assert.Empty(h.Google.Gets);
        Assert.Equal(TaskStatus.InProgress, h.Item("t1").Status);
    }
}
