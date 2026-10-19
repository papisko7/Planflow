using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using Quartz;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

public class SyncCalendarJobTests
{
    private static PlanFlowDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PlanFlowDbContext(options);
    }

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

    private static (User User, Team Team) SeedUserWithTeam(PlanFlowDbContext context)
    {
        var user = new User { Email = "student@example.com", DisplayName = "Student", PasswordHash = "hash", EmailVerifiedAtUtc = DateTime.UtcNow };
        var team = new Team { Name = "Thesis Team" };
        context.Users.Add(user);
        context.Teams.Add(team);
        context.TeamMembers.Add(new TeamMember { UserId = user.Id, TeamId = team.Id, Role = TeamRole.Owner });
        context.SaveChanges();
        return (user, team);
    }

    private static CalendarIntegration SeedActiveIntegration(PlanFlowDbContext context, Guid userId, DateTime? lastSyncedAtUtc = null)
    {
        var integration = new CalendarIntegration
        {
            UserId = userId,
            Provider = CalendarProvider.Google,
            ExternalAccountId = "google-sub",
            ExternalAccountEmail = "student@example.com",
            EncryptedAccessToken = "access-token",
            EncryptedRefreshToken = "refresh-token",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1), // not near expiry — no refresh needed
            IsActive = true,
            LastSyncedAtUtc = lastSyncedAtUtc
        };
        context.CalendarIntegrations.Add(integration);
        context.SaveChanges();
        return integration;
    }

    private static SyncCalendarJob CreateJob(
        PlanFlowDbContext context,
        Mock<IGoogleCalendarClient> calendarClient,
        Mock<IGoogleOAuthClient>? oauthClient = null,
        Mock<ITokenEncryptionService>? encryption = null,
        Mock<ICacheService>? cache = null)
    {
        oauthClient ??= new Mock<IGoogleOAuthClient>();
        encryption ??= CreatePassthroughEncryption();
        cache ??= CreateCache();

        var tokenProvider = new GoogleAccessTokenProvider(oauthClient.Object, encryption.Object, context);
        return new SyncCalendarJob(context, tokenProvider, calendarClient.Object, cache.Object, new PlanFlow.Application.Notifications.NotificationService(context), Mock.Of<Microsoft.Extensions.Logging.ILogger<SyncCalendarJob>>());
    }

    private static Mock<ICacheService> CreateCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return cache;
    }

    [Fact]
    public async Task Execute_NewEvent_CreatesTaskWithMappedFields()
    {
        var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context);
        var integration = SeedActiveIntegration(context, user.Id);

        var deadline = DateTime.UtcNow.AddDays(3);
        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync("access-token", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GoogleCalendarEvent("event-1", "Thesis defense prep", deadline.AddHours(-1), deadline, IsCancelled: false)]);

        var job = CreateJob(context, calendarClient);
        await job.Execute(CreateJobContext().Object);

        var task = await context.CalendarItems.SingleAsync();
        Assert.Equal(team.Id, task.TeamId);
        Assert.Equal(user.Id, task.AssignedUserId);
        Assert.Equal("Thesis defense prep", task.Title);
        Assert.Equal(TaskStatus.Confirmed, task.Status);
        Assert.Equal(TaskType.Event, task.TaskType); // a calendar entry is an Event, not a Task
        Assert.Equal(deadline.AddHours(-1), task.StartUtc);
        Assert.Equal(deadline, task.EndUtc);
        Assert.Null(task.DeadlineUtc);
        Assert.Equal(integration.Id, task.SourceCalendarIntegrationId);
        Assert.Equal("event-1", task.ExternalCalendarEventId);
        Assert.True(context.UrgencyScoreLogs.Any(l => l.CalendarItemId == task.Id && l.TriggerSource == ScoreTriggerSource.CalendarSync));
    }

    [Fact]
    public async Task Execute_RunTwiceWithSameEvent_NeverCreatesADuplicateTask()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);

        var calendarEvent = new GoogleCalendarEvent("event-1", "Recurring sync test", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), IsCancelled: false);
        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([calendarEvent]);

        var job = CreateJob(context, calendarClient);

        await job.Execute(CreateJobContext().Object);
        await job.Execute(CreateJobContext().Object);

        Assert.Equal(1, await context.CalendarItems.CountAsync());
    }

    [Fact]
    public async Task Execute_EventUpdatedSinceLastSync_UpdatesExistingTaskInstead()
    {
        var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context);
        var integration = SeedActiveIntegration(context, user.Id);

        var existingTask = new CalendarItem
        {
            TeamId = team.Id,
            AssignedUserId = user.Id,
            Title = "Old title",
            Status = TaskStatus.Backlog,
            DeadlineUtc = DateTime.UtcNow.AddDays(1),
            SourceCalendarIntegrationId = integration.Id,
            ExternalCalendarEventId = "event-1"
        };
        context.CalendarItems.Add(existingTask);
        await context.SaveChangesAsync();

        var newDeadline = DateTime.UtcNow.AddDays(5);
        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GoogleCalendarEvent("event-1", "Updated title", newDeadline.AddHours(-1), newDeadline, IsCancelled: false)]);

        var job = CreateJob(context, calendarClient);
        await job.Execute(CreateJobContext().Object);

        Assert.Equal(1, await context.CalendarItems.CountAsync());
        var task = await context.CalendarItems.SingleAsync();
        Assert.Equal("Updated title", task.Title);
        Assert.Equal(newDeadline.AddHours(-1), task.StartUtc);
        Assert.Equal(newDeadline, task.EndUtc);
        Assert.Equal(TaskType.Event, task.TaskType); // a row imported before 7.4 (a Task with a deadline) becomes an Event
        Assert.Null(task.DeadlineUtc);
    }

    [Fact]
    public async Task Execute_EventCancelledOnGoogle_MarksExistingTaskCancelled()
    {
        var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context);
        var integration = SeedActiveIntegration(context, user.Id);

        context.CalendarItems.Add(new CalendarItem
        {
            TeamId = team.Id,
            AssignedUserId = user.Id,
            Title = "Meeting",
            Status = TaskStatus.Backlog,
            DeadlineUtc = DateTime.UtcNow.AddDays(1),
            SourceCalendarIntegrationId = integration.Id,
            ExternalCalendarEventId = "event-1"
        });
        await context.SaveChangesAsync();

        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GoogleCalendarEvent("event-1", "Meeting", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), IsCancelled: true)]);

        var job = CreateJob(context, calendarClient);
        await job.Execute(CreateJobContext().Object);

        var task = await context.CalendarItems.SingleAsync();
        Assert.Equal(TaskStatus.Cancelled, task.Status);
    }

    [Fact]
    public async Task Execute_CancelledEventNeverImported_IsSkippedWithoutCreatingATask()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);

        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GoogleCalendarEvent("event-1", "Never imported", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), IsCancelled: true)]);

        var job = CreateJob(context, calendarClient);
        await job.Execute(CreateJobContext().Object);

        Assert.Equal(0, await context.CalendarItems.CountAsync());
    }

    [Fact]
    public async Task Execute_AccessTokenExpired_RefreshesTokenBeforeCallingCalendarApi()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        var integration = SeedActiveIntegration(context, user.Id);
        integration.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5); // already expired
        await context.SaveChangesAsync();

        var oauthClient = new Mock<IGoogleOAuthClient>();
        oauthClient.Setup(c => c.RefreshAccessTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("new-access-token", RefreshToken: null, ExpiresInSeconds: 3600, Scope: "calendar.readonly"));

        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync("new-access-token", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var job = CreateJob(context, calendarClient, oauthClient);
        await job.Execute(CreateJobContext().Object);

        calendarClient.Verify(c => c.ListEventsAsync("new-access-token", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
        var stored = await context.CalendarIntegrations.SingleAsync();
        Assert.Equal("new-access-token", stored.EncryptedAccessToken); // passthrough encryption in this test
    }

    [Fact]
    public async Task Execute_OneIntegrationFailsWithRevokedToken_OtherIntegrationStillSyncsSuccessfully()
    {
        var context = CreateInMemoryContext();
        var (failingUser, _) = SeedUserWithTeam(context);
        var (healthyUser, healthyTeam) = SeedUserWithTeam(context);
        var failingIntegration = SeedActiveIntegration(context, failingUser.Id);
        failingIntegration.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var healthyIntegration = SeedActiveIntegration(context, healthyUser.Id);
        await context.SaveChangesAsync();

        var oauthClient = new Mock<IGoogleOAuthClient>();
        oauthClient.Setup(c => c.RefreshAccessTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PlanFlow.Application.Common.Exceptions.UnauthorizedException("refresh token revoked"));

        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync("access-token", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GoogleCalendarEvent("event-1", "Still works", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), IsCancelled: false)]);

        var job = CreateJob(context, calendarClient, oauthClient);
        await job.Execute(CreateJobContext().Object);

        Assert.Equal(1, await context.CalendarItems.CountAsync());
        var task = await context.CalendarItems.SingleAsync();
        Assert.Equal(healthyTeam.Id, task.TeamId);
    }

    [Fact]
    public async Task Execute_GoogleRejectsTheConnection_DeactivatesItAndMailsTheUserOnce()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        var integration = SeedActiveIntegration(context, user.Id);
        integration.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var oauthClient = new Mock<IGoogleOAuthClient>();
        oauthClient.Setup(c => c.RefreshAccessTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PlanFlow.Application.Common.Exceptions.UnauthorizedException("refresh token revoked"));

        var job = CreateJob(context, new Mock<IGoogleCalendarClient>(), oauthClient);
        await job.Execute(CreateJobContext().Object);
        await job.Execute(CreateJobContext().Object); // the next sync no longer includes it

        Assert.False((await context.CalendarIntegrations.SingleAsync()).IsActive);
        var mail = Assert.Single(context.EmailOutbox);
        Assert.Equal(NotificationEvent.CalendarConnectionLost, mail.Event);
        Assert.Equal(user.Email, mail.ToEmail);
    }

    [Fact]
    public async Task Execute_GoogleOutage_DoesNotDeactivateOrMail()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);

        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Google is down"));

        await CreateJob(context, calendarClient).Execute(CreateJobContext().Object);

        Assert.True((await context.CalendarIntegrations.SingleAsync()).IsActive); // a temporary failure is retried, not given up on
        Assert.Empty(context.EmailOutbox);
    }

    [Fact]
    public async Task Execute_UserHasNoTeam_SkipsIntegrationWithoutThrowing()
    {
        var context = CreateInMemoryContext();
        var user = new User { Email = "teamless@example.com", DisplayName = "Teamless", PasswordHash = "hash", EmailVerifiedAtUtc = DateTime.UtcNow };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        SeedActiveIntegration(context, user.Id);

        var calendarClient = new Mock<IGoogleCalendarClient>();
        var job = CreateJob(context, calendarClient);

        await job.Execute(CreateJobContext().Object);

        Assert.Equal(0, await context.CalendarItems.CountAsync());
        calendarClient.Verify(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_InactiveIntegration_IsNeverPolled()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        var integration = SeedActiveIntegration(context, user.Id);
        integration.IsActive = false;
        await context.SaveChangesAsync();

        var calendarClient = new Mock<IGoogleCalendarClient>();
        var job = CreateJob(context, calendarClient);

        await job.Execute(CreateJobContext().Object);

        calendarClient.Verify(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Event import, [planflow] tag, IsDataIncomplete ----------

    private static Mock<IGoogleCalendarClient> ClientReturning(params GoogleCalendarEvent[] events)
    {
        var client = new Mock<IGoogleCalendarClient>();
        client.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);
        return client;
    }

    [Fact]
    public async Task Execute_TaggedEvent_ImportsDescriptionImpactAndEffort_AndIsNotIncomplete()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);
        var start = DateTime.UtcNow.AddDays(2);

        var client = ClientReturning(new GoogleCalendarEvent(
            "event-1", "Sprint review [planflow] impact=6", start, start.AddHours(1), IsCancelled: false,
            Description: "Agenda<br>[planflow] impact=8 effort=2h<br>Bring laptop"));
        await CreateJob(context, client).Execute(CreateJobContext().Object);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(TaskType.Event, item.TaskType);
        Assert.Equal("Sprint review", item.Title);                 // the tag is not part of the visible title
        Assert.Equal("Agenda\nBring laptop", item.Description);     // the tag line only fills the fields; it is not kept in the text
        Assert.Equal(6, item.ImpactScore);                         // title tag wins over the description tag
        Assert.Equal(2, item.EstimatedEffortHours);
        Assert.False(item.IsDataIncomplete);
        Assert.Empty(context.Notifications);                       // nothing to remind about
    }

    [Fact]
    public async Task Execute_UntaggedEvents_AreFlaggedAndOneNotificationListsTheirNames()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);
        var start = DateTime.UtcNow.AddDays(2);

        var client = ClientReturning(
            new GoogleCalendarEvent("e1", "Dentist", start, start.AddHours(1), IsCancelled: false),
            new GoogleCalendarEvent("e2", "Gym", start, start.AddHours(1), IsCancelled: false),
            new GoogleCalendarEvent("e3", "Complete one", start, start.AddHours(1), IsCancelled: false, Description: "[planflow] impact=5"));
        await CreateJob(context, client).Execute(CreateJobContext().Object);

        Assert.Equal(new[] { "Dentist", "Gym" }, context.CalendarItems.Where(i => i.IsDataIncomplete).Select(i => i.Title).OrderBy(t => t));

        var inbox = Assert.Single(context.Notifications);
        Assert.Equal(NotificationEvent.CalendarItemsIncomplete, inbox.Event);
        Assert.Equal(user.Id, inbox.UserId);
        Assert.Contains("\"Dentist\"", inbox.Body);
        Assert.Contains("\"Gym\"", inbox.Body);
        Assert.DoesNotContain("Complete one", inbox.Body);
        Assert.Null(inbox.TaskId);                                  // several items: nothing single to open

        var mail = Assert.Single(context.EmailOutbox);
        Assert.Equal("2 imported events need your details", mail.Subject);
    }

    [Fact]
    public async Task Execute_SingleIncompleteEvent_NotificationOpensThatItem()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);
        var start = DateTime.UtcNow.AddDays(1);

        await CreateJob(context, ClientReturning(new GoogleCalendarEvent("e1", "Dentist", start, start.AddHours(1), IsCancelled: false)))
            .Execute(CreateJobContext().Object);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(item.Id, (await context.Notifications.SingleAsync()).TaskId);
    }

    [Fact]
    public async Task Execute_LongList_ShowsTenNamesAndCountsTheRest()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);
        var start = DateTime.UtcNow.AddDays(1);

        var events = Enumerable.Range(1, 13)
            .Select(i => new GoogleCalendarEvent($"e{i}", $"Event {i}", start, start.AddHours(1), IsCancelled: false))
            .ToArray();
        await CreateJob(context, ClientReturning(events)).Execute(CreateJobContext().Object);

        var body = (await context.Notifications.SingleAsync()).Body;
        Assert.Contains("\"Event 10\"", body);
        Assert.DoesNotContain("\"Event 11\"", body);
        Assert.Contains("and 3 more", body);
    }

    [Fact]
    public async Task Execute_ResyncOfAnAlreadyFlaggedEvent_DoesNotNotifyAgain()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);
        var start = DateTime.UtcNow.AddDays(1);
        var job = CreateJob(context, ClientReturning(new GoogleCalendarEvent("e1", "Dentist", start, start.AddHours(1), IsCancelled: false)));

        await job.Execute(CreateJobContext().Object);
        await job.Execute(CreateJobContext().Object);

        Assert.Single(context.Notifications);
        Assert.True((await context.CalendarItems.SingleAsync()).IsDataIncomplete); // still waiting for the user
    }

    [Fact]
    public async Task Execute_TagAddedInGoogleLater_FillsTheDataAndClearsTheFlag()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);
        var start = DateTime.UtcNow.AddDays(1);

        await CreateJob(context, ClientReturning(new GoogleCalendarEvent("e1", "Dentist", start, start.AddHours(1), IsCancelled: false)))
            .Execute(CreateJobContext().Object);
        await CreateJob(context, ClientReturning(new GoogleCalendarEvent("e1", "Dentist", start, start.AddHours(1), IsCancelled: false, Description: "[planflow] impact=7")))
            .Execute(CreateJobContext().Object);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(7, item.ImpactScore);
        Assert.False(item.IsDataIncomplete);
    }

    [Fact]
    public async Task Execute_GoogleUpdateAfterTheUserFilledItIn_KeepsTheUsersImpactAndDoesNotReflag()
    {
        var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context);
        var integration = SeedActiveIntegration(context, user.Id);
        var start = DateTime.UtcNow.AddDays(1);
        context.CalendarItems.Add(new CalendarItem
        {
            TeamId = team.Id, AssignedUserId = user.Id, Title = "Dentist", TaskType = TaskType.Event,
            StartUtc = start, EndUtc = start.AddHours(1), ImpactScore = 9, IsDataIncomplete = false, // saved by the user
            SourceCalendarIntegrationId = integration.Id, ExternalCalendarEventId = "e1"
        });
        await context.SaveChangesAsync();

        // Google reports a change (new title) and has no tag.
        await CreateJob(context, ClientReturning(new GoogleCalendarEvent("e1", "Dentist (moved)", start, start.AddHours(1), IsCancelled: false)))
            .Execute(CreateJobContext().Object);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal("Dentist (moved)", item.Title);
        Assert.Equal(9, item.ImpactScore);
        Assert.False(item.IsDataIncomplete);
        Assert.Empty(context.Notifications);
    }

    [Fact]
    public async Task Execute_EventWithoutEndTime_IsFlaggedEvenWhenImpactIsGiven()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);

        await CreateJob(context, ClientReturning(new GoogleCalendarEvent(
                "e1", "Open-ended", DateTime.UtcNow.AddDays(1), EndUtc: null, IsCancelled: false, Description: "[planflow] impact=4")))
            .Execute(CreateJobContext().Object);

        var item = await context.CalendarItems.SingleAsync();
        Assert.Null(item.EndUtc);
        Assert.True(item.IsDataIncomplete);
    }

    [Fact]
    public async Task Execute_EventThatAlreadyEnded_IsNotFlagged()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);
        var start = DateTime.UtcNow.AddDays(-2);

        await CreateJob(context, ClientReturning(new GoogleCalendarEvent("e1", "Past meeting", start, start.AddHours(1), IsCancelled: false)))
            .Execute(CreateJobContext().Object);

        Assert.False((await context.CalendarItems.SingleAsync()).IsDataIncomplete); // over: nothing left to rank
        Assert.Empty(context.Notifications);
    }

    [Fact]
    public async Task Execute_EventWithoutStart_IsSkipped()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);

        await CreateJob(context, ClientReturning(new GoogleCalendarEvent("e1", "No start", StartUtc: null, EndUtc: null, IsCancelled: false)))
            .Execute(CreateJobContext().Object);

        Assert.Empty(context.CalendarItems);
    }

    [Fact]
    public async Task Execute_CalendarIssuesSwitchedOff_NoNotificationIsQueuedButTheFlagStays()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, user.Id);
        context.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, CalendarIssues = false });
        await context.SaveChangesAsync();
        var start = DateTime.UtcNow.AddDays(1);

        await CreateJob(context, ClientReturning(new GoogleCalendarEvent("e1", "Dentist", start, start.AddHours(1), IsCancelled: false)))
            .Execute(CreateJobContext().Object);

        Assert.True((await context.CalendarItems.SingleAsync()).IsDataIncomplete);
        Assert.Empty(context.Notifications);
        Assert.Empty(context.EmailOutbox);
    }

    // ---------- Template, colour, place, all-day, series, status, reminders, full pass ----------

    private static (PlanFlowDbContext Context, CalendarIntegration Integration) SetUp()
    {
        var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context);
        return (context, SeedActiveIntegration(context, user.Id));
    }

    private static async Task SyncAsync(PlanFlowDbContext context, params GoogleCalendarEvent[] events) =>
        await CreateJob(context, ClientReturning(events)).Execute(CreateJobContext().Object);

    private static DateTime Midnight(int daysAhead) => DateTime.UtcNow.Date.AddDays(daysAhead);

    [Fact]
    public async Task Execute_AllDayEvent_IsStoredAsDatesWithTheAllDayFlag()
    {
        var (context, _) = SetUp();

        await SyncAsync(context, new GoogleCalendarEvent("e1", "Conference", Midnight(3), Midnight(5), IsCancelled: false, IsAllDay: true));

        var item = await context.CalendarItems.SingleAsync();
        Assert.True(item.IsAllDay);
        Assert.Equal(Midnight(3), item.StartUtc);
        Assert.Equal(Midnight(5), item.EndUtc);   // Google's exclusive end: it covers the 3rd and the 4th day
    }

    [Fact]
    public async Task Execute_OutOfOffice_IsAnInformationalItem_NeverIncomplete_AndOnlyKeepsWhatItsTemplateAllows()
    {
        var (context, _) = SetUp();

        await SyncAsync(context, new GoogleCalendarEvent(
            "e1", Title: null, Midnight(10), Midnight(14), IsCancelled: false, IsAllDay: true, EventType: "outOfOffice",
            Description: "[planflow] impact=9 effort=1h", Location: "Paris", ColorId: "11", IsFree: true,
            ReminderMinutes: [1440]));

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(EventTemplate.OutOfOffice, item.EventTemplate);
        Assert.Equal("Out of office", item.Title);          // the template's own title when Google sends none
        Assert.False(item.IsDataIncomplete);                // there is nothing to rank, so nothing to complete
        Assert.Empty(context.Notifications);
        Assert.Null(item.ImpactScore);                      // a tag on an informational item is ignored
        Assert.Null(item.EstimatedEffortHours);
        Assert.Null(item.Location);                         // the template has no place field
        Assert.Null(item.Description);                      // ...and no description
        Assert.Equal("tomato", item.ColorKey);              // colour is allowed
        Assert.True(item.IsBusy);                           // Busy/Free does not exist for it
        Assert.Empty(context.ItemReminders);                // ...and neither do alerts
    }

    [Fact]
    public async Task Execute_CarriesGooglesPrivacyFlag_OnImportAndOnLaterChange()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent("e1", "Dentist", start, start.AddHours(1), IsCancelled: false, IsPrivate: true));
        Assert.True((await context.CalendarItems.SingleAsync()).IsPrivate);

        // Google's visibility changed: the next sync follows it, so a title that became public is not hidden for good.
        await SyncAsync(context, new GoogleCalendarEvent("e1", "Dentist", start, start.AddHours(1), IsCancelled: false, IsPrivate: false));
        Assert.False((await context.CalendarItems.SingleAsync()).IsPrivate);
    }

    [Fact]
    public async Task Execute_FocusTime_KeepsPlaceAndReminders_AndIsAlwaysBusy()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent(
            "e1", "Focus time", start, start.AddHours(2), IsCancelled: false, EventType: "focusTime",
            Location: "Library", IsFree: true, ReminderMinutes: [15],
            SettingsText: "Auto-decline: declines all conflicting invitations"));

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(EventTemplate.FocusTime, item.EventTemplate);
        Assert.Equal("Library", item.Location);
        Assert.True(item.IsBusy);                           // Google says "free", the template says always busy
        Assert.Equal("Auto-decline: declines all conflicting invitations", item.ExternalSettings);
        Assert.Equal(15, Assert.Single(context.ItemReminders).OffsetMinutes);
        Assert.False(item.IsDataIncomplete);
    }

    [Fact]
    public async Task Execute_WorkingLocation_KeepsTheLabel_ButHasNoColour()
    {
        var (context, _) = SetUp();

        await SyncAsync(context, new GoogleCalendarEvent(
            "e1", Title: "", Midnight(1), Midnight(2), IsCancelled: false, IsAllDay: true, EventType: "workingLocation",
            Location: "Home", ColorId: "5"));

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(EventTemplate.WorkingLocation, item.EventTemplate);
        Assert.Equal("Home", item.Location);
        Assert.Null(item.ColorKey);
        Assert.Equal("Working location", item.Title);
    }

    [Fact]
    public async Task Execute_DefaultEvent_MapsColourPlaceFreeTimeAndSeries()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent(
            "e1_20261020", "Stand-up", start, start.AddMinutes(15), IsCancelled: false,
            ColorId: "7", Location: "  Room 4  ", IsFree: true, RecurringEventId: "e1"));

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(EventTemplate.Default, item.EventTemplate);
        Assert.Equal("peacock", item.ColorKey);             // Google's colorId 7
        Assert.Equal("Room 4", item.Location);
        Assert.False(item.IsBusy);                          // transparent = free
        Assert.Equal("e1", item.SeriesId);                  // every occurrence points at the series master
    }

    [Theory]
    [InlineData(null)]
    [InlineData("99")]      // a colour Google may add one day
    public async Task Execute_UnknownOrMissingColour_FallsBackToTheDefaultColour(string? colorId)
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent("e1", "Plain", start, start.AddHours(1), IsCancelled: false, ColorId: colorId));

        Assert.Null((await context.CalendarItems.SingleAsync()).ColorKey);
    }

    [Fact]
    public async Task Execute_TooLongTitleAndPlace_AreCutToTheirColumnsInsteadOfFailingTheSync()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent(
            "e1", new string('T', 400), start, start.AddHours(1), IsCancelled: false, Location: new string('L', 900)));

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(256, item.Title.Length);
        Assert.Equal(512, item.Location!.Length);
    }

    // --- tag and text ---

    [Fact]
    public async Task Execute_UnchangedGoogleText_NeverOverwritesImpactTheUserEdited()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);
        GoogleCalendarEvent Event(DateTime end) =>
            new("e1", "Review", start, end, IsCancelled: false, Description: "Notes\n[planflow] impact=5 effort=2h");

        await SyncAsync(context, Event(start.AddHours(1)));
        var item = await context.CalendarItems.SingleAsync();
        item.ImpactScore = 9;                       // the user changed it in the app
        item.EstimatedEffortHours = 4;
        await context.SaveChangesAsync();

        await SyncAsync(context, Event(start.AddHours(2)));   // Google moved the end; its text is the same, tag included

        item = await context.CalendarItems.SingleAsync();
        Assert.Equal(9, item.ImpactScore);
        Assert.Equal(4, item.EstimatedEffortHours);
        Assert.Equal(start.AddHours(2), item.EndUtc);         // the rest of the event still follows Google
    }

    [Fact]
    public async Task Execute_TagTextChangedInGoogle_IsAppliedAgain()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent("e1", "Review", start, start.AddHours(1), false, Description: "[planflow] impact=5"));
        var item = await context.CalendarItems.SingleAsync();
        item.ImpactScore = 9;
        await context.SaveChangesAsync();

        await SyncAsync(context, new GoogleCalendarEvent("e1", "Review", start, start.AddHours(1), false, Description: "[planflow] impact=3"));

        Assert.Equal(3, (await context.CalendarItems.SingleAsync()).ImpactScore);
    }

    // --- status ---

    [Fact]
    public async Task Execute_TentativeEvent_IsImportedAsTentative()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent("e1", "Maybe", start, start.AddHours(1), false, Status: "tentative"));

        var item = await context.CalendarItems.SingleAsync();
        Assert.Equal(TaskStatus.Tentative, item.Status);
        Assert.Equal("tentative", item.ExternalStatus);
    }

    [Fact]
    public async Task Execute_StatusTheUserChose_SurvivesAnUnrelatedGoogleEdit_ButFollowsARealStatusChange()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);
        GoogleCalendarEvent Event(string title, string status) => new("e1", title, start, start.AddHours(1), false, Status: status);

        await SyncAsync(context, Event("Dentist", "confirmed"));
        var item = await context.CalendarItems.SingleAsync();
        item.Status = TaskStatus.Cancelled;          // the user decided not to go
        await context.SaveChangesAsync();

        await SyncAsync(context, Event("Dentist (moved)", "confirmed"));   // Google's status did not change
        Assert.Equal(TaskStatus.Cancelled, (await context.CalendarItems.SingleAsync()).Status);

        await SyncAsync(context, Event("Dentist (moved)", "tentative"));   // now it did
        Assert.Equal(TaskStatus.Tentative, (await context.CalendarItems.SingleAsync()).Status);
    }

    // --- reminders ---

    [Fact]
    public async Task Execute_TimedEventReminders_AreStoredAsOffsets()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent("e1", "Meeting", start, start.AddHours(1), false, ReminderMinutes: [10, 1440]));

        Assert.Equal([10, 1440], context.ItemReminders.Select(r => r.OffsetMinutes!.Value).OrderBy(m => m));
    }

    [Fact]
    public async Task Execute_AllDayEventReminders_AreStoredAsDaysBeforeAtATimeOfDay()
    {
        var (context, _) = SetUp();

        await SyncAsync(context, new GoogleCalendarEvent(
            "e1", "Holiday", Midnight(5), Midnight(6), false, IsAllDay: true, ReminderMinutes: [900]));

        var reminder = Assert.Single(context.ItemReminders);
        Assert.Equal(1, reminder.DaysBefore);
        Assert.Equal(9 * 60, reminder.TimeOfDayMinutes);     // 900 minutes before midnight = the day before at 09:00
        Assert.Null(reminder.OffsetMinutes);
    }

    [Fact]
    public async Task Execute_MoreThanFourReminders_KeepsTheFirstFour()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent("e1", "Busy", start, start.AddHours(1), false, ReminderMinutes: [5, 10, 15, 20, 25, 30]));

        Assert.Equal(4, await context.ItemReminders.CountAsync());
    }

    [Fact]
    public async Task Execute_SameRemindersAgain_AreNotRewritten()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);
        GoogleCalendarEvent Event() => new("e1", "Meeting", start, start.AddHours(1), false, ReminderMinutes: [10, 60]);

        await SyncAsync(context, Event());
        var idsBefore = await context.ItemReminders.Select(r => r.Id).OrderBy(i => i).ToListAsync();
        await SyncAsync(context, Event());

        Assert.Equal(idsBefore, await context.ItemReminders.Select(r => r.Id).OrderBy(i => i).ToListAsync());
    }

    [Fact]
    public async Task Execute_RemindersChangedInGoogle_ReplaceTheStoredOnes_AndNoneClearsThem()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);
        GoogleCalendarEvent Event(params int[] minutes) => new("e1", "Meeting", start, start.AddHours(1), false, ReminderMinutes: minutes);

        await SyncAsync(context, Event(10));
        await SyncAsync(context, Event(30, 120));
        Assert.Equal([30, 120], context.ItemReminders.Select(r => r.OffsetMinutes!.Value).OrderBy(m => m));

        await SyncAsync(context, Event());   // Google now has none
        Assert.Empty(context.ItemReminders);
    }

    [Fact]
    public async Task Execute_ReminderThatStaysInGoogle_KeepsItsRow_AndItsMemoryOfHavingGoneOff()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);
        GoogleCalendarEvent Event(params int[] minutes) => new("e1", "Meeting", start, start.AddHours(1), false, ReminderMinutes: minutes);

        await SyncAsync(context, Event(10, 60));
        var ten = await context.ItemReminders.SingleAsync(r => r.OffsetMinutes == 10);
        ten.LastFiredForUtc = start.AddMinutes(-10);
        await context.SaveChangesAsync();

        await SyncAsync(context, Event(10, 120)); // 60 became 120; 10 is untouched

        var kept = await context.ItemReminders.SingleAsync(r => r.OffsetMinutes == 10);
        Assert.Equal(ten.Id, kept.Id);
        Assert.Equal(start.AddMinutes(-10), kept.LastFiredForUtc);
        Assert.Equal([10, 120], context.ItemReminders.Select(r => r.OffsetMinutes!.Value).OrderBy(m => m));
    }

    [Fact]
    public async Task Execute_SyncThatSaysNothingAboutReminders_LeavesTheStoredOnesAlone()
    {
        var (context, _) = SetUp();
        var start = DateTime.UtcNow.AddDays(1);

        await SyncAsync(context, new GoogleCalendarEvent("e1", "Meeting", start, start.AddHours(1), false, ReminderMinutes: [10]));
        await SyncAsync(context, new GoogleCalendarEvent("e1", "Meeting", start, start.AddHours(1), false, ReminderMinutes: null));

        Assert.Single(context.ItemReminders);
    }

    // --- the daily full pass ---

    [Fact]
    public async Task Execute_FirstRun_IsAFullPass_AndRemembersBothMoments()
    {
        var (context, _) = SetUp();
        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var before = DateTime.UtcNow;

        await CreateJob(context, calendarClient).Execute(CreateJobContext().Object);

        calendarClient.Verify(c => c.ListEventsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
        var integration = await context.CalendarIntegrations.SingleAsync();
        Assert.InRange(integration.LastSyncedAtUtc!.Value, before, DateTime.UtcNow);
        Assert.Equal(integration.LastSyncedAtUtc, integration.LastFullSyncAtUtc);
    }

    [Fact]
    public async Task Execute_NextRunsSoonAfter_AreIncrementalFromTheLastPoll()
    {
        var (context, _) = SetUp();
        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var job = CreateJob(context, calendarClient);

        await job.Execute(CreateJobContext().Object);
        var firstPoll = (await context.CalendarIntegrations.SingleAsync()).LastSyncedAtUtc;
        await job.Execute(CreateJobContext().Object);

        calendarClient.Verify(c => c.ListEventsAsync(It.IsAny<string>(), firstPoll, It.IsAny<CancellationToken>()), Times.Once);
        var integration = await context.CalendarIntegrations.SingleAsync();
        Assert.True(integration.LastSyncedAtUtc > firstPoll);
        Assert.Equal(firstPoll, integration.LastFullSyncAtUtc);   // an incremental poll does not count as the full pass
    }

    [Fact]
    public async Task Execute_AfterADay_TheWindowIsFetchedInFullAgain()
    {
        var (context, integration) = SetUp();
        integration.LastSyncedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        integration.LastFullSyncAtUtc = DateTime.UtcNow.AddHours(-25);
        await context.SaveChangesAsync();
        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await CreateJob(context, calendarClient).Execute(CreateJobContext().Object);

        calendarClient.Verify(c => c.ListEventsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True((await context.CalendarIntegrations.SingleAsync()).LastFullSyncAtUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Execute_FailedRun_DoesNotMoveEitherMoment()
    {
        var (context, _) = SetUp();
        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Google is down"));

        await CreateJob(context, calendarClient).Execute(CreateJobContext().Object);

        var integration = await context.CalendarIntegrations.SingleAsync();
        Assert.Null(integration.LastSyncedAtUtc);       // so the next run repeats the full pass
        Assert.Null(integration.LastFullSyncAtUtc);
    }

    // --- clean-up of items beyond the import window ---

    private static CalendarItem ImportedItem(PlanFlowDbContext context, CalendarIntegration integration, string eventId, DateTime start, string? seriesId = "birthday-series")
    {
        var item = new CalendarItem
        {
            TeamId = context.Teams.Single().Id,
            AssignedUserId = integration.UserId,
            Title = "Anna's birthday",
            TaskType = TaskType.Event,
            Status = TaskStatus.Confirmed,
            StartUtc = start,
            EndUtc = start.AddDays(1),
            IsAllDay = true,
            SeriesId = seriesId,
            SourceCalendarIntegrationId = integration.Id,
            ExternalCalendarEventId = eventId
        };
        context.CalendarItems.Add(item);
        context.SaveChanges();
        return item;
    }

    [Fact]
    public async Task Execute_FullPass_DeletesImportedItemsBeyondTheWindow_AndKeepsTheOthers()
    {
        var (context, integration) = SetUp();
        var inside = ImportedItem(context, integration, "in-window", Midnight(30));
        var past = ImportedItem(context, integration, "past", Midnight(-400));
        ImportedItem(context, integration, "far-1", Midnight(IGoogleCalendarClient.WindowDays + 5));
        ImportedItem(context, integration, "far-2", Midnight(365 * 3));
        // An item of the person's own that happens to be far ahead is not Google's to clean up.
        var own = new CalendarItem { TeamId = context.Teams.Single().Id, Title = "Retirement party", TaskType = TaskType.Event, Status = TaskStatus.Confirmed, StartUtc = Midnight(2000) };
        context.CalendarItems.Add(own);
        await context.SaveChangesAsync();

        await SyncAsync(context); // the first run is a full pass

        var left = (await context.CalendarItems.ToListAsync()).Select(i => i.Id).ToHashSet();
        Assert.Equal(new HashSet<Guid> { inside.Id, past.Id, own.Id }, left);
    }

    [Fact]
    public async Task Execute_FullPass_TreatsAnOldImportedTaskByItsDeadline()
    {
        var (context, integration) = SetUp();
        // Rows imported before events existed were Tasks with a deadline and no start.
        var old = ImportedItem(context, integration, "legacy", Midnight(1000));
        old.TaskType = TaskType.Task;
        old.StartUtc = null;
        old.EndUtc = null;
        old.DeadlineUtc = Midnight(1000);
        await context.SaveChangesAsync();

        await SyncAsync(context);

        Assert.Empty(context.CalendarItems);
    }

    [Fact]
    public async Task Execute_FullPass_KeepsAnItemGoogleMovedIntoTheWindowInThisVeryPass()
    {
        var (context, integration) = SetUp();
        var far = ImportedItem(context, integration, "moved", Midnight(IGoogleCalendarClient.WindowDays + 40));
        var newStart = Midnight(10);

        await SyncAsync(context, new GoogleCalendarEvent("moved", "Anna's birthday", newStart, newStart.AddDays(1), IsCancelled: false, IsAllDay: true));

        var kept = await context.CalendarItems.SingleAsync();
        Assert.Equal(far.Id, kept.Id);
        Assert.Equal(newStart, kept.StartUtc);
    }

    [Fact]
    public async Task Execute_IncrementalPoll_LeavesFarItemsAlone()
    {
        var (context, integration) = SetUp();
        integration.LastSyncedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        integration.LastFullSyncAtUtc = DateTime.UtcNow.AddHours(-2); // the full pass was recent: this run is only a poll
        await context.SaveChangesAsync();
        ImportedItem(context, integration, "far", Midnight(IGoogleCalendarClient.WindowDays + 5));

        await SyncAsync(context);

        Assert.Single(context.CalendarItems);
    }

    [Fact]
    public async Task Execute_FullPass_DropsTheBlockerLinkOfAnItemItDeletes()
    {
        var (context, integration) = SetUp();
        var far = ImportedItem(context, integration, "far", Midnight(IGoogleCalendarClient.WindowDays + 5));
        var mine = new CalendarItem { TeamId = far.TeamId, Title = "Prepare", TaskType = TaskType.Task, DeadlineUtc = Midnight(3), BlockedByTaskId = far.Id };
        context.CalendarItems.Add(mine);
        await context.SaveChangesAsync();

        await SyncAsync(context);

        var kept = await context.CalendarItems.SingleAsync();
        Assert.Equal(mine.Id, kept.Id);
        Assert.Null(kept.BlockedByTaskId);
    }

    // --- Google is fetched only on the "Sync now" button, for the pressing user's account ---

    [Fact]
    public async Task Execute_SyncNowPressed_SyncsOnlyThePressingUsersAccount()
    {
        var context = CreateInMemoryContext();
        var (pressing, pressingTeam) = SeedUserWithTeam(context);
        var (bystander, _) = SeedUserWithTeam(context);
        var pressingIntegration = SeedActiveIntegration(context, pressing.Id);
        var bystanderIntegration = SeedActiveIntegration(context, bystander.Id);
        var calendarClient = ClientReturning(new GoogleCalendarEvent("e1", "Standup", DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(3), IsCancelled: false));

        await CreateJob(context, calendarClient).Execute(CreateJobContext(pressing.Id).Object);

        calendarClient.Verify(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(pressingTeam.Id, (await context.CalendarItems.SingleAsync()).TeamId);
        Assert.NotNull((await context.CalendarIntegrations.SingleAsync(i => i.Id == pressingIntegration.Id)).LastSyncedAtUtc);
        Assert.Null((await context.CalendarIntegrations.SingleAsync(i => i.Id == bystanderIntegration.Id)).LastSyncedAtUtc);
    }

    [Fact]
    public async Task Execute_SyncNowPressedByAUserWithoutAnAccount_SyncsNothing()
    {
        var context = CreateInMemoryContext();
        var (connected, _) = SeedUserWithTeam(context);
        SeedActiveIntegration(context, connected.Id);
        var calendarClient = ClientReturning(new GoogleCalendarEvent("e1", "Standup", DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(3), IsCancelled: false));

        await CreateJob(context, calendarClient).Execute(CreateJobContext(Guid.NewGuid()).Object);

        calendarClient.Verify(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(context.CalendarItems);
    }

    [Fact]
    public async Task Execute_SyncNowPressedDaysAfterTheLastFullPass_IsAFullPassAgain()
    {
        var (context, integration) = SetUp();
        integration.LastSyncedAtUtc = DateTime.UtcNow.AddHours(-30);
        integration.LastFullSyncAtUtc = DateTime.UtcNow.AddHours(-30);
        await context.SaveChangesAsync();
        var calendarClient = ClientReturning();

        await CreateJob(context, calendarClient).Execute(CreateJobContext(integration.UserId).Object);

        calendarClient.Verify(c => c.ListEventsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True((await context.CalendarIntegrations.SingleAsync()).LastFullSyncAtUtc > DateTime.UtcNow.AddMinutes(-1));
    }
}
