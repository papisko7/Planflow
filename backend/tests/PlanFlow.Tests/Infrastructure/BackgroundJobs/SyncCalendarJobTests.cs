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
        var user = new User { Email = "student@example.com", DisplayName = "Student", PasswordHash = "hash" };
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
        return new SyncCalendarJob(context, tokenProvider, calendarClient.Object, cache.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<SyncCalendarJob>>());
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

        var task = await context.Tasks.SingleAsync();
        Assert.Equal(team.Id, task.TeamId);
        Assert.Equal(user.Id, task.AssignedUserId);
        Assert.Equal("Thesis defense prep", task.Title);
        Assert.Equal(TaskStatus.Todo, task.Status);
        Assert.Equal(deadline, task.DeadlineUtc);
        Assert.Equal(integration.Id, task.SourceCalendarIntegrationId);
        Assert.Equal("event-1", task.ExternalCalendarEventId);
        Assert.True(context.UrgencyScoreLogs.Any(l => l.TaskItemId == task.Id && l.TriggerSource == ScoreTriggerSource.CalendarSync));
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

        Assert.Equal(1, await context.Tasks.CountAsync());
    }

    [Fact]
    public async Task Execute_EventUpdatedSinceLastSync_UpdatesExistingTaskInstead()
    {
        var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context);
        var integration = SeedActiveIntegration(context, user.Id);

        var existingTask = new TaskItem
        {
            TeamId = team.Id,
            AssignedUserId = user.Id,
            Title = "Old title",
            Status = TaskStatus.Todo,
            DeadlineUtc = DateTime.UtcNow.AddDays(1),
            SourceCalendarIntegrationId = integration.Id,
            ExternalCalendarEventId = "event-1"
        };
        context.Tasks.Add(existingTask);
        await context.SaveChangesAsync();

        var newDeadline = DateTime.UtcNow.AddDays(5);
        var calendarClient = new Mock<IGoogleCalendarClient>();
        calendarClient.Setup(c => c.ListEventsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GoogleCalendarEvent("event-1", "Updated title", newDeadline.AddHours(-1), newDeadline, IsCancelled: false)]);

        var job = CreateJob(context, calendarClient);
        await job.Execute(CreateJobContext().Object);

        Assert.Equal(1, await context.Tasks.CountAsync());
        var task = await context.Tasks.SingleAsync();
        Assert.Equal("Updated title", task.Title);
        Assert.Equal(newDeadline, task.DeadlineUtc);
    }

    [Fact]
    public async Task Execute_EventCancelledOnGoogle_MarksExistingTaskCancelled()
    {
        var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context);
        var integration = SeedActiveIntegration(context, user.Id);

        context.Tasks.Add(new TaskItem
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

        var task = await context.Tasks.SingleAsync();
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

        Assert.Equal(0, await context.Tasks.CountAsync());
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

        Assert.Equal(1, await context.Tasks.CountAsync());
        var task = await context.Tasks.SingleAsync();
        Assert.Equal(healthyTeam.Id, task.TeamId);
    }

    [Fact]
    public async Task Execute_UserHasNoTeam_SkipsIntegrationWithoutThrowing()
    {
        var context = CreateInMemoryContext();
        var user = new User { Email = "teamless@example.com", DisplayName = "Teamless", PasswordHash = "hash" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        SeedActiveIntegration(context, user.Id);

        var calendarClient = new Mock<IGoogleCalendarClient>();
        var job = CreateJob(context, calendarClient);

        await job.Execute(CreateJobContext().Object);

        Assert.Equal(0, await context.Tasks.CountAsync());
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
}
