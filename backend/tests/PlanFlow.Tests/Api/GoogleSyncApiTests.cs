using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Calendar.Queries.GetGoogleCalendarStatus;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Integration.PostgresIntegration;
using Quartz;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Step 7.5.9 through the real host: Google is fetched only when "Sync now" is pressed, for the caller's own account, and never twice within
/// a minute. The fake tokens in these accounts make a started sync fail on the spot without reaching Google, which is all these tests need.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GoogleSyncApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public GoogleSyncApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid UserId)> UserAsync(DateTime? connectedWithLastSync, bool connected = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var team = new Team { Name = "Personal", IsPersonal = true };
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Sync user", PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        db.Teams.Add(team);
        db.Users.Add(user);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.Id, Role = TeamRole.Owner });
        if (connected)
        {
            db.CalendarIntegrations.Add(new CalendarIntegration
            {
                UserId = user.Id,
                Provider = CalendarProvider.Google,
                ExternalAccountId = "sub-" + user.Id,
                ExternalAccountEmail = user.Email,
                EncryptedAccessToken = "not-a-real-token",
                EncryptedRefreshToken = "not-a-real-token",
                AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                LastSyncedAtUtc = connectedWithLastSync,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        var (token, _) = jwt.GenerateAccessToken(user, TeamRole.Owner, team.Id);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, user.Id);
    }

    [Fact]
    public async Task TheGoogleSyncJobsHaveNoTimer_WhileTheOtherBackgroundJobsStillDo()
    {
        var scheduler = await _factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        Assert.Empty(await scheduler.GetTriggersOfJob(GoogleSyncTrigger.CalendarJob));
        Assert.Empty(await scheduler.GetTriggersOfJob(GoogleSyncTrigger.TasksJob));
        Assert.True(await scheduler.CheckExists(GoogleSyncTrigger.CalendarJob)); // stored, waiting for a press
        Assert.NotEmpty(await scheduler.GetTriggersOfJob(new JobKey("PrioritizationJob")));
    }

    [Fact]
    public async Task SyncNow_WithoutAGoogleAccount_Is404()
    {
        var (client, _) = await UserAsync(null, connected: false);

        var response = await client.PostAsync("api/calendar/google/sync", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SyncNow_MomentsAfterTheLastSync_StartsNothing()
    {
        var (client, userId) = await UserAsync(DateTime.UtcNow.AddSeconds(-5));

        var response = await client.PostAsync("api/calendar/google/sync", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<GoogleSyncResponse>())!.Started);
        var scheduler = await _factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        Assert.False(await GoogleSyncTrigger.IsRunningAsync(scheduler, userId, CancellationToken.None));
    }

    [Fact]
    public async Task SyncNow_AfterALongPause_StartsTheSync_AndTheStatusSaysSyncingUntilItIsOver()
    {
        var (client, _) = await UserAsync(DateTime.UtcNow.AddDays(-2));

        var response = await client.PostAsync("api/calendar/google/sync", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<GoogleSyncResponse>())!.Started);

        // The sync fails at once (fake tokens), so within moments the status must report that nothing is running any more.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        GoogleCalendarStatusDto status;
        do
        {
            status = (await client.GetFromJsonAsync<GoogleCalendarStatusDto>("api/calendar/google/status"))!;
            Assert.True(status.Connected);
            if (status.Syncing)
            {
                await Task.Delay(100);
            }
        }
        while (status.Syncing && DateTime.UtcNow < deadline);
        Assert.False(status.Syncing);
    }
}
