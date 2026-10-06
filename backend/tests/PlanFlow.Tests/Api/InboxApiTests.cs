using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Infrastructure.Push;
using PlanFlow.Tests.Integration.PostgresIntegration;
using Quartz;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Through the real HTTP pipeline: an assignment lands in the assignee's inbox, the bell count and "read" work, the phone
/// registers for pushes and the push job delivers the very same entry. Users are seeded straight into the database
/// (the auth endpoints have a strict rate limit shared by the whole test host).
/// </summary>
[Collection(PostgresCollection.Name)]
public class InboxApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Phone = "ExponentPushToken[inbox-api-phone]";

    private readonly CustomWebApplicationFactory _factory;

    public InboxApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<User> SeedUserAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = name, PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private HttpClient ClientFor(User user, Guid? teamId = null, TeamRole? role = null)
    {
        using var scope = _factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwt.GenerateAccessToken(user, role, teamId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Inbox_RequiresLogin()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/notifications/unread-count")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("api/notifications/push-token", new { token = Phone, platform = "ios" })).StatusCode);
    }

    [Fact]
    public async Task AssigningATask_FillsTheInbox_TheBellAndThePush_ThenReadClearsTheBell()
    {
        var admin = await SeedUserAsync("Admin");
        var member = await SeedUserAsync("Member");
        Guid teamId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            var team = new Team { Name = "Inbox team" };
            db.Teams.Add(team);
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = admin.Id, Role = TeamRole.Owner });
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
            await db.SaveChangesAsync();
            teamId = team.Id;
        }

        var asAdmin = ClientFor(admin, teamId, TeamRole.Owner);
        var asMember = ClientFor(member, teamId, TeamRole.Member);

        // The member's phone signs up for pushes first (idempotent: twice is fine).
        Assert.Equal(HttpStatusCode.NoContent, (await asMember.PutAsJsonAsync("api/notifications/push-token", new { token = Phone, platform = "ios" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await asMember.PutAsJsonAsync("api/notifications/push-token", new { token = Phone, platform = "ios" })).StatusCode);

        var create = await asAdmin.PostAsJsonAsync($"api/teams/{teamId}/tasks", new { title = "Prepare the demo", assignedUserId = member.Id });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // 1) The assignee sees it in the inbox; the actor does not get an entry about their own action.
        var inbox = await asMember.GetFromJsonAsync<JsonElement>("api/notifications");
        var entry = Assert.Single(inbox.EnumerateArray());
        Assert.Equal((int)NotificationEvent.TaskAssignedToYou, entry.GetProperty("event").GetInt32());
        Assert.Contains("Prepare the demo", entry.GetProperty("title").GetString());
        Assert.False(entry.GetProperty("isRead").GetBoolean());
        Assert.Equal(teamId, entry.GetProperty("teamId").GetGuid());
        Assert.NotEqual(Guid.Empty, entry.GetProperty("taskId").GetGuid());
        Assert.Empty((await asAdmin.GetFromJsonAsync<JsonElement>("api/notifications")).EnumerateArray());

        // 2) The bell shows 1.
        Assert.Equal(1, (await asMember.GetFromJsonAsync<JsonElement>("api/notifications/unread-count")).GetProperty("count").GetInt32());

        // 3) The push job hands the same entry to the push service, with the bell number as the app-icon badge.
        using (var scope = _factory.Services.CreateScope())
        {
            var job = ActivatorUtilities.CreateInstance<PushDeliveryJob>(scope.ServiceProvider);
            var jobContext = new Mock<IJobExecutionContext>();
            jobContext.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            await job.Execute(jobContext.Object);
        }

        var push = Assert.Single(_factory.Services.GetRequiredService<InMemoryPushSender>().Sent, m => m.To == Phone);
        Assert.Contains("Prepare the demo", push.Title);
        Assert.Equal(1, push.Badge);
        Assert.Equal(entry.GetProperty("id").GetString(), push.Data["notificationId"]);

        // 4) Reading it clears the bell; reading somebody else's entry is a 404.
        var id = entry.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await asAdmin.PostAsync($"api/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await asMember.PostAsync($"api/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(0, (await asMember.GetFromJsonAsync<JsonElement>("api/notifications/unread-count")).GetProperty("count").GetInt32());
        Assert.True((await asMember.GetFromJsonAsync<JsonElement>("api/notifications"))[0].GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task PushToken_RejectsGarbage_AndSignOutRemovesIt()
    {
        var user = await SeedUserAsync("Phone owner");
        var client = ClientFor(user);
        const string token = "ExponentPushToken[sign-out-phone]";

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("api/notifications/push-token", new { token = "garbage", platform = "ios" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("api/notifications/push-token", new { token, platform = "android" })).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            Assert.Single(await scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>().PushTokens.Where(t => t.Token == token).ToListAsync());
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"api/notifications/push-token?token={Uri.EscapeDataString(token)}")).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>().PushTokens.Where(t => t.Token == token).ToListAsync());
        }
    }

    [Fact]
    public async Task MarkAllRead_ClearsTheBell()
    {
        var user = await SeedUserAsync("Reader");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            db.Notifications.AddRange(
                new Notification { UserId = user.Id, Event = NotificationEvent.TaskChanged, Title = "a", Body = "a" },
                new Notification { UserId = user.Id, Event = NotificationEvent.TaskChanged, Title = "b", Body = "b" });
            await db.SaveChangesAsync();
        }

        var client = ClientFor(user);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>("api/notifications/unread-count")).GetProperty("count").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("api/notifications/read-all", null)).StatusCode);

        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("api/notifications/unread-count")).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Preferences_IncludeThePushSwitch_OnByDefault()
    {
        var user = await SeedUserAsync("Prefs");
        var client = ClientFor(user);

        var defaults = await client.GetFromJsonAsync<JsonElement>("api/users/me/notification-preferences");
        Assert.True(defaults.GetProperty("pushEnabled").GetBoolean());

        var update = new
        {
            emailEnabled = true, pushEnabled = false, loginAlerts = 0, taskAssignments = true, taskChanges = true, teamTasks = false,
            taskUrgency = true, teamMembership = true, chatDigest = false, calendarIssues = true
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("api/users/me/notification-preferences", update)).StatusCode);

        var saved = await client.GetFromJsonAsync<JsonElement>("api/users/me/notification-preferences");
        Assert.False(saved.GetProperty("pushEnabled").GetBoolean());
    }
}
