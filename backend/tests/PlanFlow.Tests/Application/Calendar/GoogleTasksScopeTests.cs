using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Calendar.Commands.DisconnectGoogleCalendar;
using PlanFlow.Application.Calendar.Commands.GoogleOAuthCallback;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Calendar.Queries.GetGoogleCalendarStatus;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.Persistence;
using Xunit;

namespace PlanFlow.Tests.Application.Calendar;

/// <summary>
/// Step 7.5.3: an account only syncs Google Tasks once Google reports the tasks permission. Accounts connected before that permission
/// existed have to reconnect, and the app learns this from the status endpoint.
/// </summary>
public class GoogleTasksScopeTests
{
    private const string CalendarOnly = "openid email https://www.googleapis.com/auth/calendar.readonly";
    private const string CalendarAndTasks = CalendarOnly + " " + CalendarIntegration.TasksScope;

    private static PlanFlowDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PlanFlowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CalendarIntegration Integration(Guid userId, string scopes, bool active = true) => new()
    {
        UserId = userId,
        Provider = CalendarProvider.Google,
        ExternalAccountId = "sub-" + userId,
        ExternalAccountEmail = "user@example.com",
        EncryptedAccessToken = "enc(access)",
        EncryptedRefreshToken = "enc(refresh)",
        AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
        GrantedScopes = scopes,
        IsActive = active
    };

    // ---- what the account agreed to ----

    [Theory]
    [InlineData(CalendarAndTasks, true)]
    [InlineData(CalendarOnly, false)]
    [InlineData("", false)]
    [InlineData("https://www.googleapis.com/auth/tasks.readonly.extra", false)] // exact scope, not a prefix
    public void HasTasksScope_RequiresTheExactScope(string scopes, bool expected) =>
        Assert.Equal(expected, new CalendarIntegration { GrantedScopes = scopes }.HasTasksScope);

    [Fact]
    public async Task Callback_StoresTheScopesGoogleReports_SoADeclinedTasksPermissionIsNoticed()
    {
        var db = CreateDb();
        var userId = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<OAuthStateEntry>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new OAuthStateEntry(userId, "verifier"));
        var google = new Mock<IGoogleOAuthClient>();
        google.Setup(c => c.ExchangeCodeAsync("code", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("access", "refresh", 3600, CalendarOnly)); // the user unticked Google Tasks
        google.Setup(c => c.GetUserInfoAsync("access", It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo("sub", "user@example.com"));
        var encryption = new Mock<ITokenEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => $"enc({s})");

        await new GoogleOAuthCallbackCommandHandler(db, google.Object, cache.Object, encryption.Object)
            .Handle(new GoogleOAuthCallbackCommand("code", "state"), CancellationToken.None);

        var stored = await db.CalendarIntegrations.SingleAsync();
        Assert.Equal(CalendarOnly, stored.GrantedScopes);
        Assert.False(stored.HasTasksScope);
    }

    [Fact]
    public async Task Callback_ReconnectingWithTheTasksPermission_UpgradesTheSameRow()
    {
        var db = CreateDb();
        var userId = Guid.NewGuid();
        db.CalendarIntegrations.Add(Integration(userId, CalendarOnly));
        await db.SaveChangesAsync();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<OAuthStateEntry>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new OAuthStateEntry(userId, "verifier"));
        var google = new Mock<IGoogleOAuthClient>();
        google.Setup(c => c.ExchangeCodeAsync("code", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("access", "refresh", 3600, CalendarAndTasks));
        google.Setup(c => c.GetUserInfoAsync("access", It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo("sub", "user@example.com"));
        var encryption = new Mock<ITokenEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => $"enc({s})");

        await new GoogleOAuthCallbackCommandHandler(db, google.Object, cache.Object, encryption.Object)
            .Handle(new GoogleOAuthCallbackCommand("code", "state"), CancellationToken.None);

        var stored = await db.CalendarIntegrations.SingleAsync();
        Assert.True(stored.HasTasksScope);
    }

    [Fact]
    public async Task Disconnect_ForgetsTheGrantedScopes()
    {
        var db = CreateDb();
        var userId = Guid.NewGuid();
        db.CalendarIntegrations.Add(Integration(userId, CalendarAndTasks));
        await db.SaveChangesAsync();

        await new DisconnectGoogleCalendarCommandHandler(db).Handle(new DisconnectGoogleCalendarCommand(userId), CancellationToken.None);

        Assert.Equal(string.Empty, (await db.CalendarIntegrations.SingleAsync()).GrantedScopes);
    }

    // ---- the status the app reads ----

    [Fact]
    public async Task Status_AccountWithTheTasksPermission_ReportsTasksConnectedAndItsLastSync()
    {
        var db = CreateDb();
        var userId = Guid.NewGuid();
        var integration = Integration(userId, CalendarAndTasks);
        integration.LastTasksSyncedAtUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        db.CalendarIntegrations.Add(integration);
        await db.SaveChangesAsync();

        var status = await new GetGoogleCalendarStatusQueryHandler(db).Handle(new GetGoogleCalendarStatusQuery(userId), CancellationToken.None);

        Assert.True(status.Connected);
        Assert.True(status.TasksConnected);
        Assert.Equal(integration.LastTasksSyncedAtUtc, status.LastTasksSyncedAtUtc);
    }

    [Fact]
    public async Task Status_AccountConnectedBeforeGoogleTasks_IsConnectedButNeedsToReconnectForTasks()
    {
        var db = CreateDb();
        var userId = Guid.NewGuid();
        db.CalendarIntegrations.Add(Integration(userId, scopes: string.Empty)); // as every row looked before this migration
        await db.SaveChangesAsync();

        var status = await new GetGoogleCalendarStatusQueryHandler(db).Handle(new GetGoogleCalendarStatusQuery(userId), CancellationToken.None);

        Assert.True(status.Connected);
        Assert.False(status.TasksConnected);
    }

    [Fact]
    public async Task Status_NotConnected_ReportsNothingConnected()
    {
        var status = await new GetGoogleCalendarStatusQueryHandler(CreateDb()).Handle(new GetGoogleCalendarStatusQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(status.Connected);
        Assert.False(status.TasksConnected);
    }

    // ---- token provider ----

    private static GoogleAccessTokenProvider Provider(PlanFlowDbContext db, Mock<IGoogleOAuthClient> oauth)
    {
        var encryption = new Mock<ITokenEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
        encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
        return new GoogleAccessTokenProvider(oauth.Object, encryption.Object, db);
    }

    [Fact]
    public async Task Refresh_KeepsGrantedScopesCurrent_ButNeverBlanksThemWhenGoogleSendsNone()
    {
        var db = CreateDb();
        var integration = Integration(Guid.NewGuid(), CalendarOnly);
        db.CalendarIntegrations.Add(integration);
        await db.SaveChangesAsync();
        var oauth = new Mock<IGoogleOAuthClient>();
        oauth.SetupSequence(c => c.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("a1", null, 3600, CalendarAndTasks))
            .ReturnsAsync(new GoogleTokenResult("a2", null, 3600, string.Empty));
        var provider = Provider(db, oauth);

        await provider.RefreshAccessTokenAsync(integration, CancellationToken.None);
        Assert.True(integration.HasTasksScope);

        await provider.RefreshAccessTokenAsync(integration, CancellationToken.None);
        Assert.True(integration.HasTasksScope);
    }

    [Fact]
    public async Task Call_RejectedToken_IsRefreshedAndRetriedExactlyOnce()
    {
        var db = CreateDb();
        var integration = Integration(Guid.NewGuid(), CalendarAndTasks);
        integration.EncryptedAccessToken = "stale";
        db.CalendarIntegrations.Add(integration);
        await db.SaveChangesAsync();
        var oauth = new Mock<IGoogleOAuthClient>();
        oauth.Setup(c => c.RefreshAccessTokenAsync("enc(refresh)", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("fresh", null, 3600, CalendarAndTasks));
        var tokensSeen = new List<string>();

        var result = await Provider(db, oauth).CallAsync(integration, token =>
        {
            tokensSeen.Add(token);
            return token == "stale" ? throw new UnauthorizedException("no") : Task.FromResult("ok");
        }, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(["stale", "fresh"], tokensSeen);
    }

    [Fact]
    public async Task Call_RejectedTwice_GivesUpInsteadOfLooping()
    {
        var db = CreateDb();
        var integration = Integration(Guid.NewGuid(), CalendarAndTasks);
        db.CalendarIntegrations.Add(integration);
        await db.SaveChangesAsync();
        var oauth = new Mock<IGoogleOAuthClient>();
        oauth.Setup(c => c.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleTokenResult("fresh", null, 3600, CalendarAndTasks));
        var calls = 0;

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Provider(db, oauth).CallAsync<string>(integration, _ => { calls++; throw new UnauthorizedException("no"); }, CancellationToken.None));

        Assert.Equal(2, calls);
    }

    // ---- small domain pieces ----

    [Fact]
    public void AnItemFromGoogleTasks_IsExternalAndKnowsItIsATask_AnEventFromTheCalendarIsExternalButNotATask()
    {
        var task = new CalendarItem { ExternalTaskId = "t1" };
        var ev = new CalendarItem { ExternalCalendarEventId = "e1" };
        var own = new CalendarItem();

        Assert.True(task.IsExternal);
        Assert.True(task.IsGoogleTask);
        Assert.True(ev.IsExternal);
        Assert.False(ev.IsGoogleTask);
        Assert.False(own.IsExternal);
        Assert.False(own.IsGoogleTask);
    }

    [Fact]
    public void LiteralToday_IsThePersonsCalendarDayAsMidnightUtc()
    {
        var late = new DateTime(2026, 10, 18, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc), AllDayTime.LiteralToday(late, TimeZoneInfo.Utc));
        Assert.Equal(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc), AllDayTime.LiteralToday(late, AllDayTime.ZoneOrUtc("Europe/Warsaw"))); // 01:30 there
        Assert.Equal(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc), AllDayTime.LiteralToday(late, AllDayTime.ZoneOrUtc("America/Los_Angeles"))); // 16:30 there
        Assert.Equal(DateTimeKind.Utc, AllDayTime.LiteralToday(late, TimeZoneInfo.Utc).Kind);
    }
}
