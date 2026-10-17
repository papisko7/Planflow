using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Integration.PostgresIntegration;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Step 7.1 through the real HTTP pipeline: clients that predate the scheduling fields must not wipe them on edit,
/// and dates sent without a zone or with an offset are understood as UTC instead of crashing with a 500.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskSchedulingApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TaskSchedulingApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid TeamId)> OwnerWithTeamAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Owner", PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var team = new Team { Name = "Scheduling team" };
        db.Users.Add(user);
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwt.GenerateAccessToken(user, TeamRole.Owner, team.Id);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, team.Id);
    }

    private static string Iso(TimeSpan fromNow) => DateTime.UtcNow.Add(fromNow).ToString("yyyy-MM-ddTHH:mm:ss'Z'");

    [Fact]
    public async Task OldClientEdit_KeepsTheEventsStartEffortAndRecurrence()
    {
        var (client, teamId) = await OwnerWithTeamAsync();

        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new
        {
            title = "Weekly sync", impactScore = 5, taskType = 1,
            startUtc = Iso(TimeSpan.FromHours(3)), endUtc = Iso(TimeSpan.FromHours(4)),
            estimatedEffortHours = 1.0, isRecurring = true, recurrenceIntervalHours = 168
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The pre-7.1 mobile body: no scheduling fields at all.
        var edit = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Weekly sync (renamed)", status = 0, deadlineUtc = Iso(TimeSpan.FromHours(4)), impactScore = 5
        });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var body = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("taskType").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("startUtc").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("endUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("deadlineUtc").ValueKind);
        Assert.Equal(1.0, body.GetProperty("estimatedEffortHours").GetDouble());
        Assert.True(body.GetProperty("isRecurring").GetBoolean());
        Assert.Equal(168, body.GetProperty("recurrenceIntervalHours").GetInt32());
    }

    [Fact]
    public async Task NewClientEdit_CanStillClearTheSchedulingFields()
    {
        var (client, teamId) = await OwnerWithTeamAsync();

        var created = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new
        {
            title = "Report", impactScore = 5, deadlineUtc = Iso(TimeSpan.FromDays(3)), estimatedEffortHours = 6.0
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var edit = await client.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Report", status = 0, deadlineUtc = Iso(TimeSpan.FromDays(3)), impactScore = 5,
            taskType = 0, estimatedEffortHours = (double?)null, isRecurring = false
        });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var body = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("estimatedEffortHours").ValueKind);
    }

    [Fact]
    public async Task EventThatHasEnded_DropsToZero_ButAnOverdueTaskStaysAtTheMaximum()
    {
        var (client, teamId) = await OwnerWithTeamAsync();

        async Task<Guid> CreateAsync(object body)
        {
            var response = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var eventId = await CreateAsync(new { title = "Meeting", impactScore = 8, taskType = 1, startUtc = Iso(TimeSpan.FromHours(3)), endUtc = Iso(TimeSpan.FromHours(4)) });
        var taskId = await CreateAsync(new { title = "Report", impactScore = 8, deadlineUtc = Iso(TimeSpan.FromDays(2)) });

        // Time passes: both are moved into the past (editing the past is allowed, creating it is not).
        var pastEvent = await client.PutAsJsonAsync($"api/tasks/{eventId}", new
        {
            title = "Meeting", status = 0, impactScore = 8, taskType = 1,
            startUtc = Iso(TimeSpan.FromHours(-5)), endUtc = Iso(TimeSpan.FromHours(-4))
        });
        var overdueTask = await client.PutAsJsonAsync($"api/tasks/{taskId}", new
        {
            title = "Report", status = 0, impactScore = 8, taskType = 0, deadlineUtc = Iso(TimeSpan.FromDays(-3))
        });

        Assert.Equal(HttpStatusCode.OK, pastEvent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, overdueTask.StatusCode);
        Assert.Equal(0.0, (await pastEvent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("currentUrgencyScore").GetDouble());
        Assert.True((await overdueTask.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("currentUrgencyScore").GetDouble() >= 0.85);
    }

    [Theory]
    [InlineData(false)] // 2026-10-19T11:00:00      (no zone)
    [InlineData(true)]  // 2026-10-19T13:00:00+02:00 (offset)
    public async Task DatesWithoutZoneOrWithOffset_AreReadAsUtc_NotA500(bool withOffset)
    {
        var (client, teamId) = await OwnerWithTeamAsync();
        var target = DateTime.UtcNow.AddDays(2);
        var text = withOffset
            ? target.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss") + "+02:00"
            : target.ToString("yyyy-MM-ddTHH:mm:ss");

        var response = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new { title = "Zone", impactScore = 5, deadlineUtc = text });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deadlineUtc").GetDateTime();
        Assert.True(Math.Abs((stored.ToUniversalTime() - target).TotalSeconds) < 2);
    }
}
