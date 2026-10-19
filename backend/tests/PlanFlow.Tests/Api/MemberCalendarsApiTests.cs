using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Integration.PostgresIntegration;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Through the real HTTP pipeline and PostgreSQL: the sharing level of a personal calendar, the team availability grid and a
/// member's calendar. What a level hides must be absent from the response itself, not just from the screen.
/// </summary>
[Collection(PostgresCollection.Name)]
public class MemberCalendarsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string SecretTitle = "Secret oncology appointment";

    private readonly CustomWebApplicationFactory _factory;

    public MemberCalendarsApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Scene(HttpClient Owner, HttpClient Anna, HttpClient Stranger, Guid TeamId, Guid AnnaId);

    private async Task<Scene> SceneAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var team = new Team { Name = "Availability team" };
        var elsewhere = new Team { Name = "Other team" };
        var annaPersonal = new Team { Name = "Personal", IsPersonal = true };
        db.Teams.AddRange(team, elsewhere, annaPersonal);

        User NewUser(string name) => new() { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = name, PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var owner = NewUser("Owner");
        var anna = NewUser("Anna");
        var stranger = NewUser("Stranger");
        db.Users.AddRange(owner, anna, stranger);
        db.TeamMembers.AddRange(
            new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner },
            new TeamMember { TeamId = team.Id, UserId = anna.Id, Role = TeamRole.Member },
            new TeamMember { TeamId = annaPersonal.Id, UserId = anna.Id, Role = TeamRole.Owner },
            new TeamMember { TeamId = elsewhere.Id, UserId = stranger.Id, Role = TeamRole.Owner });
        db.CalendarItems.Add(new CalendarItem
        {
            TeamId = annaPersonal.Id, AssignedUserId = anna.Id, Title = SecretTitle, TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            StartUtc = DateTime.UtcNow.AddHours(-1), EndUtc = DateTime.UtcNow.AddHours(1), IsBusy = true, IsPrivate = false,
            Location = "Hospital", ExternalCalendarEventId = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();

        HttpClient ClientFor(User user, TeamRole role, Guid teamId)
        {
            var (token, _) = jwt.GenerateAccessToken(user, role, teamId);
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        return new Scene(
            ClientFor(owner, TeamRole.Owner, team.Id), ClientFor(anna, TeamRole.Member, team.Id),
            ClientFor(stranger, TeamRole.Owner, elsewhere.Id), team.Id, anna.Id);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Default_IsAvailability_TeammatesSeeBusyButNeverTheTitleOrPlace()
    {
        var s = await SceneAsync();

        var response = await s.Owner.GetAsync($"api/teams/{s.TeamId}/members/{s.AnnaId}/calendar?days=3");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(SecretTitle, body);
        Assert.DoesNotContain("Hospital", body);
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal(1, json.GetProperty("entries").GetArrayLength());
        Assert.Equal((int)MemberStatus.Busy, json.GetProperty("member").GetProperty("currentStatus").GetInt32());
    }

    [Fact]
    public async Task Details_RevealsTheTitle_AndOffHidesEverything()
    {
        var s = await SceneAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await s.Anna.PutAsJsonAsync("api/users/me/calendar-sharing", new { level = (int)CalendarSharingLevel.Details })).StatusCode);
        var details = await (await s.Owner.GetAsync($"api/teams/{s.TeamId}/members/{s.AnnaId}/calendar?days=3")).Content.ReadAsStringAsync();
        Assert.Contains(SecretTitle, details);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Anna.PutAsJsonAsync("api/users/me/calendar-sharing", new { level = (int)CalendarSharingLevel.Off })).StatusCode);
        var off = await s.Owner.GetAsync($"api/teams/{s.TeamId}/availability?days=3");
        var offBody = await off.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SecretTitle, offBody);
        var anna = JsonDocument.Parse(offBody).RootElement.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("userId").GetGuid() == s.AnnaId);
        Assert.Equal((int)MemberStatus.NoData, anna.GetProperty("currentStatus").GetInt32());
    }

    [Fact]
    public async Task Profile_ReportsTheSharingLevel()
    {
        var s = await SceneAsync();
        await s.Anna.PutAsJsonAsync("api/users/me/calendar-sharing", new { level = (int)CalendarSharingLevel.Details });

        var profile = await JsonOf(await s.Anna.GetAsync("api/users/me"));

        Assert.Equal((int)CalendarSharingLevel.Details, profile.GetProperty("calendarSharingLevel").GetInt32());
    }

    [Fact]
    public async Task UnknownSharingLevel_IsRejected()
    {
        var s = await SceneAsync();

        var response = await s.Anna.PutAsJsonAsync("api/users/me/calendar-sharing", new { level = 9 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AMemberOfAnotherTeam_IsForbidden()
    {
        var s = await SceneAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Stranger.GetAsync($"api/teams/{s.TeamId}/availability")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Stranger.GetAsync($"api/teams/{s.TeamId}/members/{s.AnnaId}/calendar")).StatusCode);
    }

    [Fact]
    public async Task TooLongAWindow_IsRejected()
    {
        var s = await SceneAsync();

        var response = await s.Owner.GetAsync($"api/teams/{s.TeamId}/availability?days=400");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        var s = await SceneAsync();

        var response = await _factory.CreateClient().GetAsync($"api/teams/{s.TeamId}/availability");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
