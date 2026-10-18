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

    private static Mock<IJobExecutionContext> CreateJobContext()
    {
        var jobContext = new Mock<IJobExecutionContext>();
        jobContext.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
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
        Assert.Equal(TaskStatus.Todo, task.Status);
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
            Status = TaskStatus.Todo,
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
            Status = TaskStatus.Todo,
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
        await job.Execute(CreateJobContext().Object); // the next 5-minute run no longer polls it

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

    // ---------- step 7.4: Event import, [planflow] tag, IsDataIncomplete ----------

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
        Assert.Equal("Agenda\n[planflow] impact=8 effort=2h\nBring laptop", item.Description);
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
}
