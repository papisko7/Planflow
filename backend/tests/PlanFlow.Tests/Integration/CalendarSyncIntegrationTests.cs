using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.ExternalServices.Google;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Infrastructure.Security;
using Quartz;

namespace PlanFlow.Tests.Integration;

/// <summary>
/// Phase 4B / Step 4.4 — exercises SyncCalendarJob against a real GoogleCalendarClient/
/// GoogleOAuthClient (including the resilience/retry handler from GoogleCalendarResilience),
/// with Google's servers replaced by <see cref="StubHttpMessageHandler"/> instead of a mocked
/// IGoogleCalendarClient interface. This is what distinguishes these from the existing interface-
/// level unit tests in Infrastructure/BackgroundJobs/SyncCalendarJobTests.cs: here the HTTP status
/// codes, JSON payloads, and retry/backoff timing are real, only the transport is faked.
/// </summary>
public class CalendarSyncIntegrationTests
{
    private static readonly GoogleOAuthOptions OAuthOptions = new()
    {
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        RedirectUri = "https://localhost:5443/api/calendar/google/callback",
        TokenEndpoint = "https://oauth2.googleapis.com/token"
    };

    private static PlanFlowDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<PlanFlowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Mock<ITokenEncryptionService> CreatePassthroughEncryption()
    {
        var encryption = new Mock<ITokenEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
        encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
        return encryption;
    }

    private static (User User, Team Team) SeedUserWithTeam(PlanFlowDbContext context, string email)
    {
        var user = new User { Email = email, DisplayName = email, PasswordHash = "hash" };
        var team = new Team { Name = $"{email}-team" };
        context.Users.Add(user);
        context.Teams.Add(team);
        context.TeamMembers.Add(new TeamMember { UserId = user.Id, TeamId = team.Id, Role = TeamRole.Owner });
        context.SaveChanges();
        return (user, team);
    }

    private static CalendarIntegration SeedActiveIntegration(PlanFlowDbContext context, Guid userId) =>
        SeedActiveIntegration(context, userId, DateTime.UtcNow.AddHours(1));

    private static CalendarIntegration SeedActiveIntegration(PlanFlowDbContext context, Guid userId, DateTime accessTokenExpiresAtUtc)
    {
        var integration = new CalendarIntegration
        {
            UserId = userId,
            Provider = CalendarProvider.Google,
            ExternalAccountId = "google-sub",
            ExternalAccountEmail = "student@example.com",
            EncryptedAccessToken = "access-token",
            EncryptedRefreshToken = "refresh-token",
            AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc,
            IsActive = true
        };
        context.CalendarIntegrations.Add(integration);
        context.SaveChanges();
        return integration;
    }

    /// <summary>Builds a real IGoogleCalendarClient wired through the production retry pipeline (GoogleCalendarResilience) onto a stub transport.</summary>
    private static (IGoogleCalendarClient Client, ServiceProvider Provider) BuildCalendarClient(StubHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>()
            .AddGoogleCalendarRetry()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IGoogleCalendarClient>(), provider);
    }

    private static IGoogleOAuthClient BuildOAuthClient(StubHttpMessageHandler handler) =>
        new GoogleOAuthClient(new HttpClient(handler), Options.Create(OAuthOptions));

    private static string SingleEventBody(string id, string title, DateTime startUtc, DateTime endUtc, bool cancelled = false) =>
        $$"""
        {
          "items": [
            {
              "id": "{{id}}",
              "summary": "{{title}}",
              "status": "{{(cancelled ? "cancelled" : "confirmed")}}",
              "start": { "dateTime": "{{startUtc:o}}" },
              "end": { "dateTime": "{{endUtc:o}}" }
            }
          ]
        }
        """;

    private static string EmptyEventsBody() => """{ "items": [] }""";

    private static string TokenResponseBody(string accessToken) =>
        $$"""{ "access_token": "{{accessToken}}", "expires_in": 3600, "token_type": "Bearer" }""";

    private static Mock<IJobExecutionContext> CreateJobContext()
    {
        var jobContext = new Mock<IJobExecutionContext>();
        jobContext.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return jobContext;
    }

    private static Mock<ICacheService> CreateNoopCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return cache;
    }

    private static SyncCalendarJob CreateJob(PlanFlowDbContext context, IGoogleCalendarClient calendarClient, IGoogleOAuthClient oauthClient)
    {
        var tokenProvider = new GoogleAccessTokenProvider(oauthClient, CreatePassthroughEncryption().Object, context);
        return new SyncCalendarJob(context, tokenProvider, calendarClient, CreateNoopCache().Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<SyncCalendarJob>>());
    }

    // --- Scenario A: successful sync -------------------------------------------------------

    [Fact]
    public async Task SuccessfulSync_MapsGoogleEventIntoTask()
    {
        using var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context, "a@example.com");
        var integration = SeedActiveIntegration(context, user.Id);

        var deadline = DateTime.UtcNow.AddDays(3);
        var calendarHandler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, SingleEventBody("event-1", "Thesis defense prep", deadline.AddHours(-1), deadline));
        var (calendarClient, calendarProvider) = BuildCalendarClient(calendarHandler);
        using var _ = calendarProvider;

        var job = CreateJob(context, calendarClient, BuildOAuthClient(new StubHttpMessageHandler()));
        await job.Execute(CreateJobContext().Object);

        var task = await context.Tasks.SingleAsync();
        Assert.Equal(team.Id, task.TeamId);
        Assert.Equal("Thesis defense prep", task.Title);
        Assert.Equal(deadline, task.DeadlineUtc);
        Assert.Equal(integration.Id, task.SourceCalendarIntegrationId);
        Assert.Single(calendarHandler.Requests);
    }

    [Fact]
    public async Task SuccessfulSync_RunTwiceWithSameExternalId_IsIdempotent()
    {
        using var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context, "idempotent@example.com");
        SeedActiveIntegration(context, user.Id);

        var start = DateTime.UtcNow;
        var body = SingleEventBody("event-1", "Recurring sync test", start, start.AddHours(1));
        var calendarHandler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, body)
            .Enqueue(HttpStatusCode.OK, body);
        var (calendarClient, calendarProvider) = BuildCalendarClient(calendarHandler);
        using var _ = calendarProvider;

        var job = CreateJob(context, calendarClient, BuildOAuthClient(new StubHttpMessageHandler()));
        await job.Execute(CreateJobContext().Object);
        await job.Execute(CreateJobContext().Object);

        Assert.Equal(1, await context.Tasks.CountAsync());
    }

    // --- Scenario B: expired/invalid token (401) triggers a refresh-and-retry --------------

    [Fact]
    public async Task ExpiredToken_401FromCalendarApi_RefreshesTokenAndRetriesSuccessfully()
    {
        using var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context, "b@example.com");
        // Locally-valid expiry so GoogleAccessTokenProvider does NOT proactively refresh —
        // the 401 has to come from Google itself and be handled reactively by SyncCalendarJob.
        var integration = SeedActiveIntegration(context, user.Id);

        var deadline = DateTime.UtcNow.AddDays(2);
        var calendarHandler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.Unauthorized)
            .Enqueue(HttpStatusCode.OK, SingleEventBody("event-1", "Post-refresh event", deadline.AddHours(-1), deadline));
        var (calendarClient, calendarProvider) = BuildCalendarClient(calendarHandler);
        using var _ = calendarProvider;

        var oauthHandler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, TokenResponseBody("refreshed-access-token"));
        var oauthClient = BuildOAuthClient(oauthHandler);

        var job = CreateJob(context, calendarClient, oauthClient);
        await job.Execute(CreateJobContext().Object);

        var task = await context.Tasks.SingleAsync();
        Assert.Equal("Post-refresh event", task.Title);
        Assert.Equal(2, calendarHandler.Requests.Count); // initial 401 + retry after refresh
        Assert.Single(oauthHandler.Requests); // exactly one forced refresh call

        var stored = await context.CalendarIntegrations.SingleAsync();
        Assert.Equal("refreshed-access-token", stored.EncryptedAccessToken); // passthrough encryption in this test
        Assert.Equal(team.Id, task.TeamId);
    }

    [Fact]
    public async Task ExpiredToken_RefreshAlsoFails_IntegrationIsSkippedWithoutCrashingTheJob()
    {
        using var context = CreateInMemoryContext();
        var (failingUser, _) = SeedUserWithTeam(context, "b-fail@example.com");
        var (healthyUser, healthyTeam) = SeedUserWithTeam(context, "b-healthy@example.com");
        SeedActiveIntegration(context, failingUser.Id);
        SeedActiveIntegration(context, healthyUser.Id);

        var calendarHandler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.Unauthorized) // failing integration's only attempt
            .Enqueue(HttpStatusCode.OK, SingleEventBody("event-1", "Still works", DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
        var (calendarClient, calendarProvider) = BuildCalendarClient(calendarHandler);
        using var _ = calendarProvider;

        // Refresh itself is rejected by Google (revoked refresh token) — the forced retry never happens.
        var oauthHandler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.BadRequest);
        var oauthClient = BuildOAuthClient(oauthHandler);

        var job = CreateJob(context, calendarClient, oauthClient);
        await job.Execute(CreateJobContext().Object);

        Assert.Equal(1, await context.Tasks.CountAsync());
        var task = await context.Tasks.SingleAsync();
        Assert.Equal(healthyTeam.Id, task.TeamId);
    }

    // --- Scenario C: 5xx / timeout fallback and per-integration isolation ------------------

    [Fact]
    public async Task ServerError_ExhaustsRetriesGracefully_DoesNotCrashJobAndIsolatesOtherIntegrations()
    {
        using var context = CreateInMemoryContext();
        var (failingUser, _) = SeedUserWithTeam(context, "c-fail@example.com");
        var (healthyUser, healthyTeam) = SeedUserWithTeam(context, "c-healthy@example.com");
        SeedActiveIntegration(context, failingUser.Id);
        SeedActiveIntegration(context, healthyUser.Id);

        // GoogleCalendarResilience retries 5xx up to 3 times (4 attempts total) before giving up.
        var calendarHandler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.InternalServerError)
            .Enqueue(HttpStatusCode.InternalServerError)
            .Enqueue(HttpStatusCode.InternalServerError)
            .Enqueue(HttpStatusCode.InternalServerError)
            .Enqueue(HttpStatusCode.OK, SingleEventBody("event-1", "Still works", DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
        var (calendarClient, calendarProvider) = BuildCalendarClient(calendarHandler);
        using var _ = calendarProvider;

        var job = CreateJob(context, calendarClient, BuildOAuthClient(new StubHttpMessageHandler()));

        var exception = await Record.ExceptionAsync(() => job.Execute(CreateJobContext().Object));

        Assert.Null(exception); // SyncCalendarJob must swallow the exhausted-retry failure, not propagate it
        Assert.Equal(1, await context.Tasks.CountAsync());
        var task = await context.Tasks.SingleAsync();
        Assert.Equal(healthyTeam.Id, task.TeamId);
    }

    [Fact]
    public async Task NetworkTimeout_IsHandledGracefully_DoesNotCorruptOtherIntegrations()
    {
        using var context = CreateInMemoryContext();
        var (failingUser, _) = SeedUserWithTeam(context, "timeout-fail@example.com");
        var (healthyUser, healthyTeam) = SeedUserWithTeam(context, "timeout-healthy@example.com");
        SeedActiveIntegration(context, failingUser.Id);
        SeedActiveIntegration(context, healthyUser.Id);

        var calendarHandler = new StubHttpMessageHandler()
            .EnqueueTimeout()
            .EnqueueTimeout()
            .EnqueueTimeout()
            .EnqueueTimeout()
            .Enqueue(HttpStatusCode.OK, EmptyEventsBody());
        var (calendarClient, calendarProvider) = BuildCalendarClient(calendarHandler);
        using var _ = calendarProvider;

        var job = CreateJob(context, calendarClient, BuildOAuthClient(new StubHttpMessageHandler()));

        var exception = await Record.ExceptionAsync(() => job.Execute(CreateJobContext().Object));

        Assert.Null(exception);
        Assert.Equal(0, await context.Tasks.CountAsync());
        var integrations = await context.CalendarIntegrations.ToListAsync();
        Assert.All(integrations, i => Assert.True(i.IsActive)); // a transient failure must never deactivate the connection
    }

    // --- Scenario D: 429 rate limiting triggers exponential backoff retry ------------------

    [Fact]
    public async Task RateLimited_429ThenSuccess_RetriesWithBackoffAndCompletesSync()
    {
        using var context = CreateInMemoryContext();
        var (user, team) = SeedUserWithTeam(context, "d@example.com");
        SeedActiveIntegration(context, user.Id);

        var deadline = DateTime.UtcNow.AddDays(1);
        var calendarHandler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.TooManyRequests)
            .Enqueue(HttpStatusCode.TooManyRequests)
            .Enqueue(HttpStatusCode.OK, SingleEventBody("event-1", "Synced after backoff", deadline.AddHours(-1), deadline));
        var (calendarClient, calendarProvider) = BuildCalendarClient(calendarHandler);
        using var _ = calendarProvider;

        var job = CreateJob(context, calendarClient, BuildOAuthClient(new StubHttpMessageHandler()));

        var startedAt = DateTime.UtcNow;
        await job.Execute(CreateJobContext().Object);
        var elapsed = DateTime.UtcNow - startedAt;

        var task = await context.Tasks.SingleAsync();
        Assert.Equal("Synced after backoff", task.Title);
        Assert.Equal(team.Id, task.TeamId);
        Assert.Equal(3, calendarHandler.Requests.Count); // 2 rate-limited attempts + 1 successful retry
        // Exponential backoff (base 100ms, 2 retries) means this could not have completed instantly.
        Assert.True(elapsed.TotalMilliseconds >= 100, $"Expected retry backoff to introduce delay, took {elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task RateLimited_ExceedsRetryBudget_SkippedGracefullyThisRun()
    {
        using var context = CreateInMemoryContext();
        var (user, _) = SeedUserWithTeam(context, "d-exhausted@example.com");
        SeedActiveIntegration(context, user.Id);

        var calendarHandler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.TooManyRequests)
            .Enqueue(HttpStatusCode.TooManyRequests)
            .Enqueue(HttpStatusCode.TooManyRequests)
            .Enqueue(HttpStatusCode.TooManyRequests); // exceeds MaxRetryAttempts = 3
        var (calendarClient, calendarProvider) = BuildCalendarClient(calendarHandler);
        using var _ = calendarProvider;

        var job = CreateJob(context, calendarClient, BuildOAuthClient(new StubHttpMessageHandler()));
        var exception = await Record.ExceptionAsync(() => job.Execute(CreateJobContext().Object));

        Assert.Null(exception);
        Assert.Equal(0, await context.Tasks.CountAsync());
    }
}
