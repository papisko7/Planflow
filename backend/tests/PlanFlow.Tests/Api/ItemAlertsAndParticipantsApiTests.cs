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

/// <summary>Through the real HTTP pipeline: alerts and participants on create, edit and detail, the "no list = no change" rule, and leaving an item.</summary>
[Collection(PostgresCollection.Name)]
public class ItemAlertsAndParticipantsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ItemAlertsAndParticipantsApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Scene(HttpClient Owner, HttpClient Anna, HttpClient Ben, Guid TeamId, Guid AnnaId, Guid BenId, Guid OwnerId);

    private async Task<Scene> SceneAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var team = new Team { Name = "Alerts team" };
        db.Teams.Add(team);

        User NewUser(string name) => new() { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = name, PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var owner = NewUser("Owner");
        var anna = NewUser("Anna");
        var ben = NewUser("Ben");
        db.Users.AddRange(owner, anna, ben);
        db.TeamMembers.AddRange(
            new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner },
            new TeamMember { TeamId = team.Id, UserId = anna.Id, Role = TeamRole.Member },
            new TeamMember { TeamId = team.Id, UserId = ben.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        HttpClient ClientFor(User user, TeamRole role)
        {
            var (token, _) = jwt.GenerateAccessToken(user, role, team.Id);
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        return new Scene(ClientFor(owner, TeamRole.Owner), ClientFor(anna, TeamRole.Member), ClientFor(ben, TeamRole.Member), team.Id, anna.Id, ben.Id, owner.Id);
    }

    private static string Iso(TimeSpan fromNow) => DateTime.UtcNow.Add(fromNow).ToString("yyyy-MM-ddTHH:mm:ss'Z'");

    private static async Task<Guid> IdOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    private async Task<Guid> CreateEventAsync(Scene s, object[]? reminders, Guid[]? guests)
    {
        var response = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new
        {
            title = "Retro", impactScore = 5, taskType = 1, assignedUserId = s.AnnaId,
            startUtc = Iso(TimeSpan.FromDays(2)), endUtc = Iso(TimeSpan.FromDays(2).Add(TimeSpan.FromHours(1))),
            reminders, participantUserIds = guests
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await IdOf(response);
    }

    [Fact]
    public async Task Create_WithAlertsAndGuests_TheDetailShowsBoth()
    {
        var s = await SceneAsync();

        var id = await CreateEventAsync(s,
            [new { offsetMinutes = 30 }, new { daysBefore = 1, timeOfDayMinutes = 540 }],
            [s.BenId]);

        var detail = await s.Owner.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");
        var reminders = detail.GetProperty("reminders").EnumerateArray().ToList();
        Assert.Equal(2, reminders.Count);
        Assert.All(reminders, r => Assert.Equal(0, r.GetProperty("anchor").GetInt32())); // counts from the start
        Assert.Contains(reminders, r => r.GetProperty("offsetMinutes").ValueKind == JsonValueKind.Number && r.GetProperty("offsetMinutes").GetInt32() == 30);
        Assert.Equal([s.BenId], detail.GetProperty("task").GetProperty("participantUserIds").EnumerateArray().Select(e => e.GetGuid()));
    }

    [Fact]
    public async Task TeamList_ShowsTheParticipantsOfEachItem()
    {
        var s = await SceneAsync();
        var id = await CreateEventAsync(s, null, [s.BenId]);

        var list = await s.Owner.GetFromJsonAsync<JsonElement>($"api/teams/{s.TeamId}/tasks");

        var item = list.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == id);
        Assert.Equal([s.BenId], item.GetProperty("participantUserIds").EnumerateArray().Select(e => e.GetGuid()));
    }

    [Fact]
    public async Task Edit_WithoutTheLists_AQuickActionLikeDone_KeepsAlertsAndGuests()
    {
        var s = await SceneAsync();
        var id = await CreateEventAsync(s, [new { offsetMinutes = 30 }], [s.BenId]);

        // What the app sends for a quick action: no reminders, no participantUserIds.
        var edit = await s.Owner.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Retro (renamed)", status = 5, assignedUserId = s.AnnaId, impactScore = 5, taskType = 1,
            startUtc = Iso(TimeSpan.FromDays(2)), endUtc = Iso(TimeSpan.FromDays(2).Add(TimeSpan.FromHours(1)))
        });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var detail = await s.Owner.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");
        Assert.Single(detail.GetProperty("reminders").EnumerateArray());
        Assert.Single(detail.GetProperty("task").GetProperty("participantUserIds").EnumerateArray());
    }

    [Fact]
    public async Task Edit_WithEmptyLists_ClearsThem()
    {
        var s = await SceneAsync();
        var id = await CreateEventAsync(s, [new { offsetMinutes = 30 }], [s.BenId]);

        var edit = await s.Owner.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Retro", status = 5, assignedUserId = s.AnnaId, impactScore = 5, taskType = 1,
            startUtc = Iso(TimeSpan.FromDays(2)), endUtc = Iso(TimeSpan.FromDays(2).Add(TimeSpan.FromHours(1))),
            reminders = Array.Empty<object>(), participantUserIds = Array.Empty<Guid>()
        });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var detail = await s.Owner.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");
        Assert.Empty(detail.GetProperty("reminders").EnumerateArray());
        Assert.Empty(detail.GetProperty("task").GetProperty("participantUserIds").EnumerateArray());
    }

    [Theory]
    [InlineData("tooMany")]
    [InlineData("negative")]
    [InlineData("bothForms")]
    [InlineData("deadlineOnAnEvent")]
    public async Task Create_ABadAlert_Is400(string kind)
    {
        var s = await SceneAsync();
        object[] reminders = kind switch
        {
            "tooMany" => [new { offsetMinutes = 1 }, new { offsetMinutes = 2 }, new { offsetMinutes = 3 }, new { offsetMinutes = 4 }, new { offsetMinutes = 5 }],
            "negative" => [new { offsetMinutes = -5 }],
            "bothForms" => [new { offsetMinutes = 5, daysBefore = 1, timeOfDayMinutes = 540 }],
            _ => [new { offsetMinutes = 5, anchor = 1 }]
        };

        var response = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new
        {
            title = "Retro", impactScore = 5, taskType = 1,
            startUtc = Iso(TimeSpan.FromDays(2)), endUtc = Iso(TimeSpan.FromDays(2).Add(TimeSpan.FromHours(1))), reminders
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AGuestOutsideTheTeam_Is400()
    {
        var s = await SceneAsync();

        var response = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new
        {
            title = "Retro", impactScore = 5, taskType = 1, startUtc = Iso(TimeSpan.FromDays(2)), participantUserIds = new[] { Guid.NewGuid() }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AMemberWhoIsOnlyAGuest_CannotEditTheItem_ButCanLeaveIt()
    {
        var s = await SceneAsync();
        var id = await CreateEventAsync(s, null, [s.BenId]);

        var edit = await s.Ben.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Hijacked", status = 5, assignedUserId = s.AnnaId, impactScore = 5, taskType = 1, startUtc = Iso(TimeSpan.FromDays(2))
        });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);

        var leave = await s.Ben.DeleteAsync($"api/tasks/{id}/participants/me");
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        var detail = await s.Owner.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");
        Assert.Empty(detail.GetProperty("task").GetProperty("participantUserIds").EnumerateArray());

        var again = await s.Ben.DeleteAsync($"api/tasks/{id}/participants/me");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode); // already out
    }

    [Fact]
    public async Task Leave_SomeoneWhoDoesNotTakePart_Is404_AndCannotEndSomeoneElsesParticipation()
    {
        var s = await SceneAsync();
        var id = await CreateEventAsync(s, null, [s.BenId]);

        var response = await s.Anna.DeleteAsync($"api/tasks/{id}/participants/me"); // Anna is the assignee, not a participant

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var detail = await s.Owner.GetFromJsonAsync<JsonElement>($"api/tasks/{id}");
        Assert.Single(detail.GetProperty("task").GetProperty("participantUserIds").EnumerateArray()); // Ben is still in
    }

    [Fact]
    public async Task TheAssigneeCanInviteCoContractorsToTheirOwnTask()
    {
        var s = await SceneAsync();
        var created = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new
        {
            title = "Report", impactScore = 5, assignedUserId = s.AnnaId, deadlineUtc = Iso(TimeSpan.FromDays(3))
        });
        var id = await IdOf(created);

        var edit = await s.Anna.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Report", status = 0, assignedUserId = s.AnnaId, impactScore = 5, deadlineUtc = Iso(TimeSpan.FromDays(3)),
            taskType = 0, participantUserIds = new[] { s.BenId }
        });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var body = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([s.BenId], body.GetProperty("participantUserIds").EnumerateArray().Select(e => e.GetGuid()));
    }
}
