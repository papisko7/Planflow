using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Integration.PostgresIntegration;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Through the real HTTP pipeline: the password-reset flow, the notification-settings endpoints and the role-change endpoint.
/// </summary>
/// <remarks>
/// The reset/login endpoints share a strict rate limit (5 per minute for the whole test host), so the reset flow is one
/// test that stays within it, and everything else here is seeded straight into the database instead of calling auth endpoints.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class NotificationApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public NotificationApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<User> SeedUserAsync(string password = "old-password-1")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Tester", PasswordHash = hasher.Hash(password), EmailVerifiedAtUtc = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<HttpClient> ClientForAsync(User user, Guid? teamId = null, TeamRole? role = null)
    {
        using var scope = _factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwt.GenerateAccessToken(user, role, teamId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task PasswordReset_FullFlow_ChangesPasswordSignsOutDevicesAndSendsTheMails()
    {
        var user = await SeedUserAsync("old-password-1");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            db.RefreshSessions.Add(new RefreshSession { UserId = user.Id, TokenHash = "device", ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();

        // 1) Asking for a reset answers 204 for a real AND an unknown address (no account enumeration).
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("api/auth/forgot-password", new { email = user.Email })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("api/auth/forgot-password", new { email = "nobody@example.com" })).StatusCode);

        string code;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            var mail = await db.EmailOutbox.SingleAsync(m => m.Event == NotificationEvent.PasswordReset && m.ToEmail == user.Email);
            code = Regex.Match(mail.TextBody, @"\b\d{6}\b").Value;
            Assert.False(await db.EmailOutbox.AnyAsync(m => m.ToEmail == "nobody@example.com"));
        }

        // 2) A wrong code is a 400 (not 401, which the app would read as "session expired").
        var wrong = code == "000000" ? "111111" : "000000";
        var wrongResponse = await client.PostAsJsonAsync("api/auth/reset-password", new { email = user.Email, code = wrong, newPassword = "new-password-2" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongResponse.StatusCode);

        // 3) The right code sets the new password.
        var ok = await client.PostAsJsonAsync("api/auth/reset-password", new { email = user.Email, code, newPassword = "new-password-2" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        // 4) The new password signs in.
        var login = await client.PostAsJsonAsync("api/auth/login", new { email = user.Email, password = "new-password-2" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            Assert.DoesNotContain(db.RefreshSessions, s => s.TokenHash == "device"); // the old device was signed out by the reset
            Assert.Contains(db.EmailOutbox, m => m.Event == NotificationEvent.PasswordChanged && m.ToEmail == user.Email);
        }
    }

    [Fact]
    public async Task NotificationPreferences_RequireLogin()
    {
        var response = await _factory.CreateClient().GetAsync("api/users/me/notification-preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NotificationPreferences_DefaultsThenSaveThenReadBack()
    {
        var user = await SeedUserAsync();
        var client = await ClientForAsync(user);

        var defaults = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("api/users/me/notification-preferences");
        Assert.True(defaults.GetProperty("emailEnabled").GetBoolean());
        Assert.False(defaults.GetProperty("teamTasks").GetBoolean());

        var update = new
        {
            emailEnabled = false, loginAlerts = 1, taskAssignments = true, taskChanges = false, teamTasks = true,
            taskUrgency = true, teamMembership = false, chatDigest = true, calendarIssues = false
        };
        var put = await client.PutAsJsonAsync("api/users/me/notification-preferences", update);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var saved = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("api/users/me/notification-preferences");
        Assert.False(saved.GetProperty("emailEnabled").GetBoolean());
        Assert.Equal(1, saved.GetProperty("loginAlerts").GetInt32());
        Assert.True(saved.GetProperty("teamTasks").GetBoolean());
        Assert.False(saved.GetProperty("teamMembership").GetBoolean());
    }

    [Fact]
    public async Task ChangeRole_OwnerCanPromoteMember_MemberCannot()
    {
        var owner = await SeedUserAsync();
        var member = await SeedUserAsync();
        var other = await SeedUserAsync();
        Guid teamId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            var team = new Team { Name = "Roles" };
            db.Teams.Add(team);
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner });
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = other.Id, Role = TeamRole.Member });
            await db.SaveChangesAsync();
            teamId = team.Id;
        }

        var asMember = await ClientForAsync(member, teamId, TeamRole.Member);
        var denied = await asMember.PutAsJsonAsync($"api/teams/{teamId}/members/{other.Id}/role", new { role = (int)TeamRole.Admin });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var asOwner = await ClientForAsync(owner, teamId, TeamRole.Owner);
        var promoted = await asOwner.PutAsJsonAsync($"api/teams/{teamId}/members/{member.Id}/role", new { role = (int)TeamRole.Admin });
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);

        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        Assert.Equal(TeamRole.Admin, (await checkDb.TeamMembers.SingleAsync(m => m.TeamId == teamId && m.UserId == member.Id)).Role);
        Assert.Contains(checkDb.EmailOutbox, m => m.Event == NotificationEvent.TeamRoleChanged && m.ToEmail == member.Email);
    }

    [Fact]
    public async Task TeamNotificationSettings_AreSavedForTheCallerAndShownInTheTeamList()
    {
        var user = await SeedUserAsync();
        Guid teamId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            var team = new Team { Name = "Muted" };
            db.Teams.Add(team);
            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.Id, Role = TeamRole.Member });
            await db.SaveChangesAsync();
            teamId = team.Id;
        }

        var client = await ClientForAsync(user, teamId, TeamRole.Member);
        var put = await client.PutAsJsonAsync($"api/teams/{teamId}/notification-settings", new { muteNotifications = true, muteChat = false });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var teams = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("api/teams");
        var mine = teams.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == teamId);
        Assert.True(mine.GetProperty("muteNotifications").GetBoolean());
        Assert.False(mine.GetProperty("muteChat").GetBoolean());
    }
}
