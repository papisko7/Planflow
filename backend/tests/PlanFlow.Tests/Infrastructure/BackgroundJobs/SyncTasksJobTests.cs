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

/// <summary>The Google Tasks import (lists, due day as deadline, first-seen day as start, unverified by default).</summary>
public class SyncTasksJobTests
{
    /// <summary>
    /// A scripted Google Tasks account: lists, their tasks (Google's own copy, which a patch changes like the real service would) and a
    /// record of how each list was asked for and which changes were sent.
    /// </summary>
    private sealed class FakeTasksClient : IGoogleTasksClient
    {
        public List<GoogleTaskList> Lists { get; } = [];
        public Dictionary<string, List<GoogleTask>> Tasks { get; } = [];
        public List<(string ListId, DateTime? Since)> TaskCalls { get; } = [];
        public List<(string ListId, string TaskId, GoogleTaskPatch Patch)> Patches { get; } = [];
        public List<string> Gets { get; } = [];
        public Exception? PatchFailure { get; set; }
        public HashSet<string> FailingLists { get; } = [];
        public string? RejectedToken { get; set; }
        public int ListsCalls { get; private set; }

        public Task<GoogleTask?> GetTaskAsync(string accessToken, string taskListId, string taskId, CancellationToken cancellationToken)
        {
            Gets.Add(taskId);
            return Task.FromResult(Tasks.GetValueOrDefault(taskListId)?.FirstOrDefault(t => t.Id == taskId));
        }

        public Task<GoogleTask> PatchTaskAsync(string accessToken, string taskListId, string taskId, GoogleTaskPatch patch, CancellationToken cancellationToken)
        {
            Patches.Add((taskListId, taskId, patch));
            if (PatchFailure is not null)
            {
                throw PatchFailure;
            }

            var list = Tasks[taskListId];
            var index = list.FindIndex(t => t.Id == taskId);
            var task = list[index];
            list[index] = task with
            {
                Title = patch.Title ?? task.Title,
                Notes = patch.Notes is null ? task.Notes : patch.Notes.Length == 0 ? null : patch.Notes,
                DueDay = patch.DueDay ?? task.DueDay,
                IsCompleted = patch.IsCompleted ?? task.IsCompleted,
                UpdatedUtc = DateTime.UtcNow
            };
            return Task.FromResult(list[index]);
        }

        public FakeTasksClient WithList(string id, string title, params GoogleTask[] tasks)
        {
            Lists.Add(new GoogleTaskList(id, title));
            Tasks[id] = [.. tasks];
            return this;
        }

        public Task<IReadOnlyList<GoogleTaskList>> ListTaskListsAsync(string accessToken, CancellationToken cancellationToken)
        {
            ListsCalls++;
            if (accessToken == RejectedToken)
            {
                throw new UnauthorizedException("rejected");
            }
            return Task.FromResult<IReadOnlyList<GoogleTaskList>>(Lists.ToList());
        }

        public Task<IReadOnlyList<GoogleTask>> ListTasksAsync(string accessToken, string taskListId, DateTime? updatedSinceUtc, CancellationToken cancellationToken)
        {
            if (accessToken == RejectedToken)
            {
                throw new UnauthorizedException("rejected");
            }
            TaskCalls.Add((taskListId, updatedSinceUtc));
            if (FailingLists.Contains(taskListId))
            {
                throw new HttpRequestException("boom");
            }
            return Task.FromResult<IReadOnlyList<GoogleTask>>(Tasks.GetValueOrDefault(taskListId)?.ToList() ?? []);
        }
    }

    private static PlanFlowDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<PlanFlowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Mock<ITokenEncryptionService> CreatePassthroughEncryption()
    {
        var encryption = new Mock<ITokenEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
        encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
        return encryption;
    }

    /// <summary>A run context; with a user id it is a "Sync now" press for that account, without one it covers every account.</summary>
    private static Mock<IJobExecutionContext> CreateJobContext(Guid? userId = null)
    {
        var jobContext = new Mock<IJobExecutionContext>();
        jobContext.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var data = new JobDataMap();
        if (userId is { } id)
        {
            data["UserId"] = id.ToString();
        }
        jobContext.SetupGet(c => c.MergedJobDataMap).Returns(data);
        return jobContext;
    }

    private static (User User, Team Team) SeedUserWithTeam(PlanFlowDbContext context, string? timeZoneId = null, string email = "student@example.com")
    {
        var user = new User { Email = email, DisplayName = "Student", PasswordHash = "hash", EmailVerifiedAtUtc = DateTime.UtcNow, TimeZoneId = timeZoneId };
        var team = new Team { Name = "Personal", IsPersonal = true };
        context.Users.Add(user);
        context.Teams.Add(team);
        context.TeamMembers.Add(new TeamMember { UserId = user.Id, TeamId = team.Id, Role = TeamRole.Owner });
        context.SaveChanges();
        return (user, team);
    }

    private static CalendarIntegration SeedIntegration(
        PlanFlowDbContext context, Guid userId, string scopes = CalendarIntegration.TasksScope, DateTime? lastTasksSyncedAtUtc = null, string suffix = "")
    {
        var integration = new CalendarIntegration
        {
            UserId = userId,
            Provider = CalendarProvider.Google,
            ExternalAccountId = "google-sub" + suffix,
            ExternalAccountEmail = "student@example.com",
            EncryptedAccessToken = "access-token",
            EncryptedRefreshToken = "refresh-token",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            GrantedScopes = scopes,
            LastTasksSyncedAtUtc = lastTasksSyncedAtUtc,
            IsActive = true
        };
        context.CalendarIntegrations.Add(integration);
        context.SaveChanges();
        return integration;
    }

    private static SyncTasksJob CreateJob(PlanFlowDbContext context, FakeTasksClient client, Mock<IGoogleOAuthClient>? oauth = null)
    {
        var tokenProvider = new GoogleAccessTokenProvider((oauth ?? new Mock<IGoogleOAuthClient>()).Object, CreatePassthroughEncryption().Object, context);
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new SyncTasksJob(
            context, tokenProvider, client, cache.Object,
            new PlanFlow.Application.Notifications.NotificationService(context),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<SyncTasksJob>>());
    }

    private static Task Run(SyncTasksJob job) => job.Execute(CreateJobContext().Object);

    /// <summary>A calendar day relative to today, in the stored all-day form (midnight UTC, read literally).</summary>
    private static DateTime Day(int daysFromToday) => AllDayTime.LiteralToday(DateTime.UtcNow, TimeZoneInfo.Utc).AddDays(daysFromToday);

    private static GoogleTask Open(string id, string title, DateTime? due, string? notes = null) =>
        new(id, title, notes, due, IsCompleted: false, IsDeleted: false);

    [Fact]
    public async Task Execute_NewOpenTask_IsImportedWithTheMappedFields()
    {
        var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context);
        var integration = SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "My Tasks", Open("t1", "Write chapter 3", Day(5), "Check the sources"));

        await Run(CreateJob(context, client));

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(TaskType.Task, item.TaskType);
        Assert.Equal("Write chapter 3", item.Title);
        Assert.Equal("Check the sources", item.Description);
        Assert.Equal(team.Id, item.TeamId);
        Assert.Equal(user.Id, item.AssignedUserId);
        Assert.Equal(TaskStatus.Backlog, item.Status);
        Assert.Equal("needsAction", item.ExternalStatus);
        Assert.True(item.IsAllDay);
        Assert.Equal(Day(5), item.DeadlineUtc);
        Assert.Equal(Day(0), item.StartUtc);                  // the day it was first seen
        Assert.False(item.IsDeadlineDefaulted);
        Assert.True(item.IsDataIncomplete);                   // Google has neither impact nor effort: unverified
        Assert.Null(item.ImpactScore);
        Assert.Equal(integration.Id, item.SourceCalendarIntegrationId);
        Assert.Equal("t1", item.ExternalTaskId);
        Assert.Null(item.ExternalCalendarEventId);
        Assert.True(item.IsExternal);
        Assert.True(item.IsGoogleTask);

        var list = await context.ExternalTaskLists.SingleAsync();
        Assert.Equal("My Tasks", list.Title);
        Assert.Equal("l1", list.ExternalId);
        Assert.Equal(list.Id, item.ExternalTaskListId);
    }

    [Fact]
    public async Task Execute_ImportsIntoThePersonalTeamEvenWhenAnotherTeamWasJoinedFirst()
    {
        var context = CreateInMemoryContext();
        var (user, personal) = SeedUserWithTeam(context);
        var older = new Team { Name = "Project" };
        context.Teams.Add(older);
        context.TeamMembers.Add(new TeamMember { UserId = user.Id, TeamId = older.Id, Role = TeamRole.Member, JoinedAtUtc = DateTime.UtcNow.AddYears(-1) });
        await context.SaveChangesAsync();
        SeedIntegration(context, user.Id);

        await Run(CreateJob(context, new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "A", Day(2)))));

        Assert.Equal(personal.Id, (await context.CalendarItems.SingleAsync()).TeamId);
    }

    [Fact]
    public async Task Execute_TaskWithoutDueDay_GetsTomorrowAndIsMarkedAsDefaulted()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);

        await Run(CreateJob(context, new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Someday", due: null))));

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(Day(1), item.DeadlineUtc);
        Assert.Equal(Day(0), item.StartUtc);
        Assert.True(item.IsDeadlineDefaulted);
    }

    [Fact]
    public async Task Execute_TaskAlreadyOverdue_StartsOnItsDueDayBecauseItCannotStartAfterTheDeadline()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);

        await Run(CreateJob(context, new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Late", Day(-9)))));

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(Day(-9), item.DeadlineUtc);
        Assert.Equal(Day(-9), item.StartUtc);
    }

    [Fact]
    public async Task Execute_FirstSeenDayIsTheDayInTheUsersOwnTimeZone()
    {
        var context = CreateInMemoryContext();
        var (aheadUser, _) = SeedUserWithTeam(context, "Pacific/Kiritimati", "ahead@example.com");   // UTC+14
        var (behindUser, _) = SeedUserWithTeam(context, "Pacific/Pago_Pago", "behind@example.com");  // UTC-11
        SeedIntegration(context, aheadUser.Id, suffix: "-a");
        SeedIntegration(context, behindUser.Id, suffix: "-b");

        await Run(CreateJob(context, new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Same task", Day(30)))));

        var ahead = await context.CalendarItems.SingleAsync(i => i.AssignedUserId == aheadUser.Id);
        var behind = await context.CalendarItems.SingleAsync(i => i.AssignedUserId == behindUser.Id);
        Assert.True(ahead.StartUtc > behind.StartUtc);        // 25 hours apart: never the same calendar day
        Assert.Equal(AllDayTime.LiteralToday(DateTime.UtcNow, AllDayTime.ZoneOrUtc("Pacific/Kiritimati")), ahead.StartUtc);
    }

    [Fact]
    public async Task Execute_CompletedOrDeletedTaskNeverSeenBefore_IsNotImported()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks",
            new GoogleTask("done", "Old", null, Day(-30), IsCompleted: true, IsDeleted: false),
            new GoogleTask("gone", "Removed", null, Day(3), IsCompleted: false, IsDeleted: true),
            Open("open", "Still to do", Day(3)));

        await Run(CreateJob(context, client));

        Assert.Equal("Still to do", (await context.CalendarItems.SingleAsync()).Title);
    }

    [Fact]
    public async Task Execute_SubTask_IsNotImported()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks",
            Open("parent", "Parent", Day(3)),
            new GoogleTask("child", "Step one", null, Day(3), IsCompleted: false, IsDeleted: false, IsSubtask: true));

        await Run(CreateJob(context, client));

        Assert.Equal("Parent", (await context.CalendarItems.SingleAsync()).Title);
    }

    [Fact]
    public async Task Execute_RunTwice_NeverDuplicatesAndNeverMovesTheStart()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(10)));
        var job = CreateJob(context, client);

        await Run(job);
        var item = await context.CalendarItems.SingleAsync();
        item.StartUtc = Day(-3); // as if the task had been first seen three days ago (or the user picked this start)
        await context.SaveChangesAsync();
        await Run(job);

        var after = await context.CalendarItems.SingleAsync();
        Assert.Equal(Day(-3), after.StartUtc);
        Assert.Equal(1, await context.ExternalTaskLists.CountAsync());
    }

    [Fact]
    public async Task Execute_TaskEditedInGoogle_UpdatesTitleNotesAndDueDay()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Draft", Day(10), "old notes"));
        var job = CreateJob(context, client);
        await Run(job);

        client.Tasks["l1"][0] = Open("t1", "Final draft", Day(12), "new notes");
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal("Final draft", item.Title);
        Assert.Equal("new notes", item.Description);
        Assert.Equal(Day(12), item.DeadlineUtc);
    }

    [Fact]
    public async Task Execute_DueDayMovedBeforeThePlannedStart_PullsTheStartBackSoItNeverFollowsTheDeadline()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(10)));
        var job = CreateJob(context, client);
        await Run(job);

        client.Tasks["l1"][0] = Open("t1", "Task", Day(-2)); // pulled forward to the past
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(Day(-2), item.DeadlineUtc);
        Assert.Equal(Day(-2), item.StartUtc);
    }

    [Fact]
    public async Task Execute_DueDayAppearsForADefaultedTask_ReplacesThePlaceholderAndClearsTheFlag()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", due: null));
        var job = CreateJob(context, client);
        await Run(job);

        client.Tasks["l1"][0] = Open("t1", "Task", Day(6));
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(Day(6), item.DeadlineUtc);
        Assert.False(item.IsDeadlineDefaulted);
    }

    [Fact]
    public async Task Execute_DueDayRemovedInGoogle_KeepsTheDeadlineWeHold()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(6)));
        var job = CreateJob(context, client);
        await Run(job);

        client.Tasks["l1"][0] = Open("t1", "Task", due: null);
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(Day(6), item.DeadlineUtc);
        Assert.False(item.IsDeadlineDefaulted);
    }

    [Fact]
    public async Task Execute_TaskCompletedInGoogleAndReopened_FollowsGoogleBothWays()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(4)));
        var job = CreateJob(context, client);
        await Run(job);

        client.Tasks["l1"][0] = new GoogleTask("t1", "Task", null, Day(4), IsCompleted: true, IsDeleted: false);
        await Run(job);
        Assert.Equal(TaskStatus.Completed, (await context.CalendarItems.SingleAsync()).Status);

        client.Tasks["l1"][0] = Open("t1", "Task", Day(4));
        await Run(job);
        Assert.Equal(TaskStatus.Backlog, (await context.CalendarItems.SingleAsync()).Status);
    }

    [Fact]
    public async Task Execute_StatusChosenInTheApp_IsNotUndoneWhileGoogleStatusStaysTheSame()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(4)));
        var job = CreateJob(context, client);
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        item.Status = TaskStatus.InProgress; // started in the app; Google still says needsAction
        await context.SaveChangesAsync();
        client.Tasks["l1"][0] = Open("t1", "Task renamed in Google", Day(4));
        await Run(job);

        var after = await context.CalendarItems.SingleAsync();
        Assert.Equal(TaskStatus.InProgress, after.Status);
        Assert.Equal("Task renamed in Google", after.Title);
    }

    [Fact]
    public async Task Execute_TaskDeletedInGoogle_IsCancelledNotErased()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(4)));
        var job = CreateJob(context, client);
        await Run(job);

        client.Tasks["l1"][0] = new GoogleTask("t1", null, null, null, IsCompleted: false, IsDeleted: true);
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(TaskStatus.Cancelled, item.Status);
        Assert.Equal("Task", item.Title); // what we know of it is kept
    }

    [Fact]
    public async Task Execute_PlanflowTagInNotes_FillsImpactAndEffort_ButOnlyWhenTheTextChanged()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(4), "Plan it\n[planflow] impact=8 effort=3h"));
        var job = CreateJob(context, client);
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(8, item.ImpactScore);
        Assert.Equal(3, item.EstimatedEffortHours);
        Assert.Equal("Plan it", item.Description);       // the tag line is not kept in the stored notes
        Assert.False(item.IsDataIncomplete);              // the tag gave both impact and effort: nothing left to confirm

        item.ImpactScore = 2; // the user rated it differently in the app
        await context.SaveChangesAsync();
        await Run(job);      // same text in Google
        Assert.Equal(2, (await context.CalendarItems.SingleAsync()).ImpactScore);

        client.Tasks["l1"][0] = Open("t1", "Task", Day(4), "Plan it\n[planflow] impact=9");
        await Run(job);      // the text changed: the tag is read again
        var changed = await context.CalendarItems.SingleAsync();
        Assert.Equal(9, changed.ImpactScore);
        Assert.Equal(3, changed.EstimatedEffortHours);    // a missing part of the tag never erases what we hold
    }

    [Fact]
    public async Task Execute_TagWithImpactOnly_StaysUnverified_UntilTheEffortArrivesInGoogle()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(4), "[planflow] impact=8"));
        var job = CreateJob(context, client);
        await Run(job);
        Assert.True((await context.CalendarItems.SingleAsync()).IsDataIncomplete);

        client.Tasks["l1"][0] = Open("t1", "Task", Day(4), "[planflow] impact=8 effort=2h");
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(2, item.EstimatedEffortHours);
        Assert.False(item.IsDataIncomplete);               // both fields are filled now, so the item is no longer flagged
    }

    [Fact]
    public async Task Execute_FullyTaggedTask_IsNotListedInTheIncompleteNotification()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks",
            Open("t1", "Tagged", Day(4), "[planflow] impact=5 effort=1h"),
            Open("t2", "Plain", Day(4)));

        await Run(CreateJob(context, client));

        var inbox = Assert.Single(context.Notifications);
        Assert.Contains("\"Plain\"", inbox.Body);
        Assert.DoesNotContain("Tagged", inbox.Body);
    }

    [Fact]
    public async Task Execute_GoogleUnchanged_KeepsWhatTheUserEditedInTheApp_ButGoogleChangesWin()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Google title", Day(4), "Google notes"));
        var job = CreateJob(context, client);
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        item.Title = "My title";
        item.Description = "My notes";
        item.DeadlineUtc = Day(9);
        await context.SaveChangesAsync();

        await Run(job);      // Google sends the same task again (e.g. another list changed)
        item = await context.CalendarItems.SingleAsync();
        Assert.Equal("My title", item.Title);
        Assert.Equal("My notes", item.Description);
        Assert.Equal(Day(9), item.DeadlineUtc);

        client.Tasks["l1"][0] = Open("t1", "Google title 2", Day(6), "Google notes");
        await Run(job);      // the task changed in Google: Google wins
        item = await context.CalendarItems.SingleAsync();
        Assert.Equal("Google title 2", item.Title);
        Assert.Equal(Day(6), item.DeadlineUtc);
    }

    [Fact]
    public async Task Execute_NewTasks_OneNotificationAndOneMailListTheirNames()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Buy paper", Day(2)), Open("t2", "Print poster", Day(3)));

        await Run(CreateJob(context, client));

        var inbox = Assert.Single(context.Notifications);
        Assert.Equal(NotificationEvent.CalendarItemsIncomplete, inbox.Event);
        Assert.Equal(user.Id, inbox.UserId);
        Assert.Contains("\"Buy paper\"", inbox.Body);
        Assert.Contains("\"Print poster\"", inbox.Body);
        Assert.Contains("Google Tasks", inbox.Body);
        Assert.Null(inbox.TaskId);
        Assert.Equal("2 imported tasks need your details", Assert.Single(context.EmailOutbox).Subject);
    }

    [Fact]
    public async Task Execute_SingleNewTask_NotificationOpensThatTask_AndNothingIsSentOnTheNextQuietRun()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Only one", Day(2)));
        var job = CreateJob(context, client);

        await Run(job);
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(item.Id, (await context.Notifications.SingleAsync()).TaskId);
    }

    [Fact]
    public async Task Execute_AccountWithoutTheTasksPermission_IsSkippedAndGoogleIsNeverAsked()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id, scopes: "openid email https://www.googleapis.com/auth/calendar.readonly");
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(2)));

        await Run(CreateJob(context, client));

        Assert.Equal(0, client.ListsCalls);
        Assert.Empty(context.CalendarItems);
    }

    [Fact]
    public async Task Execute_NewListIsReadInFull_KnownListIncrementally_AndTheWatermarkAdvances()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        var lastPoll = DateTime.UtcNow.AddMinutes(-5);
        var integration = SeedIntegration(context, user.Id, lastTasksSyncedAtUtc: lastPoll);
        context.ExternalTaskLists.Add(new ExternalTaskList { CalendarIntegrationId = integration.Id, ExternalId = "known", Title = "Known" });
        await context.SaveChangesAsync();
        var client = new FakeTasksClient().WithList("known", "Known").WithList("fresh", "Fresh");

        await Run(CreateJob(context, client));

        Assert.Contains(("known", (DateTime?)lastPoll), client.TaskCalls);
        Assert.Contains(("fresh", (DateTime?)null), client.TaskCalls); // never seen: its tasks may predate the last poll
        Assert.True((await context.CalendarIntegrations.SingleAsync()).LastTasksSyncedAtUtc > lastPoll);
        Assert.Equal(2, await context.ExternalTaskLists.CountAsync());
    }

    [Fact]
    public async Task Execute_ListThatCannotBeRead_IsSkippedWithoutStoringItOrAdvancingTheWatermark()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient()
            .WithList("bad", "Broken", Open("x", "Hidden", Day(1)))
            .WithList("good", "Fine", Open("t1", "Visible", Day(1)));
        client.FailingLists.Add("bad");

        await Run(CreateJob(context, client));

        Assert.Equal("Visible", (await context.CalendarItems.SingleAsync()).Title);
        Assert.Equal("Fine", (await context.ExternalTaskLists.SingleAsync()).Title); // the failed one has no row, so it is read in full next time
        Assert.Null((await context.CalendarIntegrations.SingleAsync()).LastTasksSyncedAtUtc);
    }

    [Fact]
    public async Task Execute_SyncNowPressed_SyncsOnlyThePressingUsersAccount()
    {
        var context = CreateInMemoryContext();
        var (pressing, _) = SeedUserWithTeam(context, email: "pressing@example.com");
        var (bystander, _) = SeedUserWithTeam(context, email: "bystander@example.com");
        SeedIntegration(context, pressing.Id, suffix: "-p");
        SeedIntegration(context, bystander.Id, suffix: "-b");
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Write chapter", Day(3)));

        await CreateJob(context, client).Execute(CreateJobContext(pressing.Id).Object);

        Assert.Equal(pressing.Id, (await context.CalendarItems.SingleAsync()).AssignedUserId);
        Assert.Equal(1, client.ListsCalls);
        var bystanderIntegration = await context.CalendarIntegrations.SingleAsync(i => i.UserId == bystander.Id);
        Assert.Null(bystanderIntegration.LastTasksSyncedAtUtc);
    }

    [Fact]
    public async Task Execute_ListRenamedInGoogle_UpdatesItsTitle()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Shopping", Open("t1", "Milk", Day(1)));
        var job = CreateJob(context, client);
        await Run(job);

        client.Lists[0] = new GoogleTaskList("l1", "Groceries");
        await Run(job);

        Assert.Equal("Groceries", (await context.ExternalTaskLists.SingleAsync()).Title);
    }

    [Fact]
    public async Task Execute_ListRemovedInGoogle_CancelsItsOpenTasksKeepsDoneOnesAndDropsTheGroup()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient()
            .WithList("keep", "Stays", Open("k1", "Safe", Day(2)))
            .WithList("gone", "Goes away", Open("g1", "Open one", Day(2)), Open("g2", "Done one", Day(2)));
        var job = CreateJob(context, client);
        await Run(job);

        var doneOne = await context.CalendarItems.SingleAsync(i => i.Title == "Done one");
        doneOne.Status = TaskStatus.Completed;
        await context.SaveChangesAsync();
        client.Lists.RemoveAll(l => l.Id == "gone");
        await Run(job);

        var items = await context.CalendarItems.ToListAsync();
        Assert.Equal(TaskStatus.Cancelled, items.Single(i => i.Title == "Open one").Status);
        Assert.Equal(TaskStatus.Completed, items.Single(i => i.Title == "Done one").Status);
        Assert.Equal(TaskStatus.Backlog, items.Single(i => i.Title == "Safe").Status);
        Assert.All(items.Where(i => i.Title != "Safe"), i => Assert.Null(i.ExternalTaskListId));
        Assert.Equal("Stays", (await context.ExternalTaskLists.SingleAsync()).Title);
    }

    [Fact]
    public async Task Execute_GoogleReportsNoListsAtAll_NeverCancelsAnything()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var client = new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Task", Day(2)));
        var job = CreateJob(context, client);
        await Run(job);

        client.Lists.Clear();
        await Run(job);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(TaskStatus.Backlog, item.Status);
        Assert.NotNull(item.ExternalTaskListId);
    }

    [Fact]
    public async Task Execute_TokenRejectedDespiteLocalExpiry_RefreshesAndRetriesOnce()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);
        var oauth = new Mock<IGoogleOAuthClient>();
        oauth.Setup(c => c.RefreshAccessTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("fresh-token", null, 3600, $"openid {CalendarIntegration.TasksScope}"));
        var client = new FakeTasksClient { RejectedToken = "access-token" }.WithList("l1", "Tasks", Open("t1", "Task", Day(2)));

        await Run(CreateJob(context, client, oauth));

        Assert.Single(context.CalendarItems);
        Assert.Equal("fresh-token", (await context.CalendarIntegrations.SingleAsync()).EncryptedAccessToken);
    }

    [Fact]
    public async Task Execute_OneAccountRejectedForGood_OtherAccountsStillSync_AndTheConnectionIsLeftActive()
    {
        var context = CreateInMemoryContext();
        var (failingUser, _) = SeedUserWithTeam(context, email: "failing@example.com");
        var (healthyUser, healthyTeam) = SeedUserWithTeam(context, email: "healthy@example.com");
        var failing = SeedIntegration(context, failingUser.Id, suffix: "-f");
        failing.EncryptedAccessToken = "dead-token";
        SeedIntegration(context, healthyUser.Id, suffix: "-h");
        var oauth = new Mock<IGoogleOAuthClient>();
        oauth.Setup(c => c.RefreshAccessTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("dead-token", null, 3600, string.Empty)); // the refreshed token is rejected too
        // Both accounts share the fake: the dead token (and the refreshed one) is rejected, the healthy account's "access-token" is accepted.
        var client = new FakeTasksClient { RejectedToken = "dead-token" }.WithList("l1", "Tasks", Open("t1", "Task", Day(2)));
        await context.SaveChangesAsync();

        await Run(CreateJob(context, client, oauth));

        Assert.Equal(healthyTeam.Id, (await context.CalendarItems.SingleAsync(i => i.AssignedUserId == healthyUser.Id)).TeamId);
        Assert.True((await context.CalendarIntegrations.SingleAsync(i => i.UserId == failingUser.Id)).IsActive); // the calendar job owns deactivation
    }

    [Fact]
    public async Task Execute_UnverifiedOverdueTask_IsScoredRightAwayButStaysBelowCritical()
    {
        var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context);
        SeedIntegration(context, user.Id);

        await Run(CreateJob(context, new FakeTasksClient().WithList("l1", "Tasks", Open("t1", "Ancient", Day(-40)))));

        var item = await context.CalendarItems.SingleAsync();
        Assert.True(context.UrgencyScoreLogs.Any(l => l.CalendarItemId == item.Id && l.TriggerSource == ScoreTriggerSource.CalendarSync));
        Assert.True(item.CurrentUrgencyScore is > 0.0 and < 0.85, $"score was {item.CurrentUrgencyScore}");
        Assert.Equal(team.Id, item.TeamId);
    }
}
