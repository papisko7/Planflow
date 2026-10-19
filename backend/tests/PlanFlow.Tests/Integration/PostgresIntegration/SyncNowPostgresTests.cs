using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Calendar.Queries.GetGoogleCalendarStatus;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.Persistence;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The "Sync now" button from end to end: the HTTP call, the gate, Quartz starting the two jobs in the real host, the real Google
/// clients reading a scripted Google, PostgreSQL storing the result, and the status endpoint reporting "syncing" until it is over.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SyncNowPostgresTests
{
    private readonly PostgresWebApplicationFactory _factory;

    public SyncNowPostgresTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Account(Guid UserId, Guid TeamId, HttpClient Client);

    private async Task<Account> ConnectedUserAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<ITokenEncryptionService>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Syncer", PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var team = new Team { Name = "Personal", IsPersonal = true };
        db.Users.Add(user);
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { UserId = user.Id, TeamId = team.Id, Role = TeamRole.Owner });
        db.CalendarIntegrations.Add(new CalendarIntegration
        {
            UserId = user.Id,
            Provider = CalendarProvider.Google,
            ExternalAccountId = "sub-" + user.Id,
            ExternalAccountEmail = user.Email,
            EncryptedAccessToken = encryption.Encrypt("access-token"), // the jobs decrypt it exactly as in production
            EncryptedRefreshToken = encryption.Encrypt("refresh-token"),
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            GrantedScopes = CalendarIntegration.TasksScope,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var (token, _) = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(user, TeamRole.Owner, team.Id);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Account(user.Id, team.Id, client);
    }

    private static async Task<GoogleCalendarStatusDto> StatusAfterSyncAsync(HttpClient client)
    {
        // Polled like the app does, only a little faster; the host allows a user 100 requests a minute, so the 30 s cap stays under it.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        GoogleCalendarStatusDto status;
        do
        {
            status = (await client.GetFromJsonAsync<GoogleCalendarStatusDto>("api/calendar/google/status"))!;
            if (status.Syncing)
            {
                await Task.Delay(300);
            }
        }
        while (status.Syncing && DateTime.UtcNow < deadline);
        Assert.False(status.Syncing, "the sync did not finish in time");
        return status;
    }

    [Fact]
    public async Task SyncNow_ImportsTheCallersCalendarAndTasks_OnceInAMinute_AndNobodyElses()
    {
        var caller = await ConnectedUserAsync();
        var bystander = await ConnectedUserAsync();
        var start = DateTime.UtcNow.AddDays(2);
        _factory.GoogleCalendar = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, GoogleJson.Events(
            GoogleJson.Event("meeting", "Supervisor meeting", start, start.AddHours(1), description: "[planflow] impact=7 effort=1h")));
        _factory.GoogleTasks = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, GoogleJson.TaskLists(("l1", "My Tasks")))
            .Enqueue(HttpStatusCode.OK, GoogleJson.Tasks(GoogleJson.Task("t1", "Write chapter 3", due: AllDayTime.LiteralToday(DateTime.UtcNow, TimeZoneInfo.Utc).AddDays(5))));

        var press = await caller.Client.PostAsync("api/calendar/google/sync", null);

        Assert.Equal(HttpStatusCode.Accepted, press.StatusCode);
        Assert.True((await press.Content.ReadFromJsonAsync<GoogleSyncResponse>())!.Started);
        var status = await StatusAfterSyncAsync(caller.Client);
        Assert.NotNull(status.LastSyncedAtUtc);
        Assert.NotNull(status.LastTasksSyncedAtUtc);

        var items = await caller.Client.GetFromJsonAsync<List<TaskDto>>($"api/teams/{caller.TeamId}/tasks");
        Assert.Equivalent(new[] { "Supervisor meeting", "Write chapter 3" }, items!.Select(i => i.Title));
        Assert.Equal(7, items!.Single(i => i.TaskType == TaskType.Event).ImpactScore);
        Assert.All(items!, i => Assert.True(i.IsExternal));

        // A second press right away is turned down without asking Google again.
        var again = await caller.Client.PostAsync("api/calendar/google/sync", null);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.False((await again.Content.ReadFromJsonAsync<GoogleSyncResponse>())!.Started);
        Assert.Single(_factory.GoogleCalendar.Requests);
        Assert.Equal(2, _factory.GoogleTasks.Requests.Count); // the lists, then the tasks of the one list

        // The other account was never touched.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        Assert.Equal(0, await db.CalendarItems.CountAsync(i => i.TeamId == bystander.TeamId));
        Assert.Null((await db.CalendarIntegrations.AsNoTracking().SingleAsync(i => i.UserId == bystander.UserId)).LastSyncedAtUtc);
    }
}
