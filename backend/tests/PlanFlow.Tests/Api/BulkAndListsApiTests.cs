using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Integration.PostgresIntegration;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Api;

/// <summary>Step 7.5.11 through the real HTTP pipeline and PostgreSQL: the bulk endpoints and the team's own lists, with the permissions of each role.</summary>
[Collection(PostgresCollection.Name)]
public class BulkAndListsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BulkAndListsApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Scene(HttpClient Owner, HttpClient Anna, HttpClient Ben, Guid TeamId, Guid OwnerId, Guid AnnaId, Guid BenId);

    private async Task<Scene> SceneAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var team = new Team { Name = "Bulk team" };
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

        return new Scene(ClientFor(owner, TeamRole.Owner), ClientFor(anna, TeamRole.Member), ClientFor(ben, TeamRole.Member), team.Id, owner.Id, anna.Id, ben.Id);
    }

    private async Task<Guid> AddTaskAsync(Scene s, Guid? assignee, string title = "Report")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var item = new CalendarItem
        {
            TeamId = s.TeamId, AssignedUserId = assignee, Title = title, TaskType = TaskType.Task, Status = TaskStatus.Backlog,
            DeadlineUtc = DateTime.UtcNow.AddDays(5), ImpactScore = 5
        };
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<int> CountAsync(Scene s)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>().CalendarItems.CountAsync(i => i.TeamId == s.TeamId);
    }

    // ---- bulk ----

    [Fact]
    public async Task BulkDelete_AMemberRemovesTheirOwn_AndTheReplyNamesWhatWasSkipped()
    {
        var s = await SceneAsync();
        var mine = await AddTaskAsync(s, s.AnnaId);
        var theirs = await AddTaskAsync(s, s.BenId);

        var response = await s.Anna.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks/bulk-delete", new { taskIds = new[] { mine, theirs } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("succeeded").GetInt32());
        Assert.Equal([theirs], body.GetProperty("skippedIds").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Equal(1, await CountAsync(s));
    }

    [Fact]
    public async Task BulkDelete_TheOwnerRemovesEveryoneElsesItems()
    {
        var s = await SceneAsync();
        var a = await AddTaskAsync(s, s.AnnaId);
        var b = await AddTaskAsync(s, s.BenId);

        var response = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks/bulk-delete", new { taskIds = new[] { a, b } });

        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("succeeded").GetInt32());
        Assert.Equal(0, await CountAsync(s));
    }

    [Fact]
    public async Task Bulk_ForAnotherTeamsUrl_IsForbidden()
    {
        var s = await SceneAsync();
        var other = await SceneAsync();

        var response = await s.Owner.PostAsJsonAsync($"api/teams/{other.TeamId}/tasks/bulk-delete", new { taskIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_AnEmptySelection_IsABadRequest()
    {
        var s = await SceneAsync();

        var response = await s.Owner.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks/bulk-status", new { taskIds = Array.Empty<Guid>(), status = 3 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkStatus_MarksTheSelectionDone_AndTheListShowsIt()
    {
        var s = await SceneAsync();
        var a = await AddTaskAsync(s, s.AnnaId);
        var b = await AddTaskAsync(s, s.AnnaId, "Second");

        var response = await s.Anna.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks/bulk-status", new { taskIds = new[] { a, b }, status = (int)TaskStatus.Completed });

        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("succeeded").GetInt32());
        var list = await s.Anna.GetFromJsonAsync<JsonElement>($"api/teams/{s.TeamId}/tasks");
        Assert.All(list.EnumerateArray(), t => Assert.Equal((int)TaskStatus.Completed, t.GetProperty("status").GetInt32()));
    }

    // ---- lists ----

    private static async Task<Guid> IdOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    [Fact]
    public async Task Lists_AMemberMakesOne_AnotherMemberCannotRename_TheOwnerCan()
    {
        var s = await SceneAsync();

        var created = await s.Anna.PostAsJsonAsync($"api/teams/{s.TeamId}/lists", new { name = "Sprint", kind = 0 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = await IdOf(created);

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Ben.PutAsJsonAsync($"api/teams/{s.TeamId}/lists/{id}", new { name = "Mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.PutAsJsonAsync($"api/teams/{s.TeamId}/lists/{id}", new { name = "Sprint 2" })).StatusCode);

        var lists = await s.Ben.GetFromJsonAsync<JsonElement>($"api/teams/{s.TeamId}/lists");
        Assert.Equal(["Sprint 2"], lists.EnumerateArray().Select(l => l.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Lists_ADuplicateNameIsAConflict()
    {
        var s = await SceneAsync();
        await s.Anna.PostAsJsonAsync($"api/teams/{s.TeamId}/lists", new { name = "Sprint", kind = 0 });

        var again = await s.Ben.PostAsJsonAsync($"api/teams/{s.TeamId}/lists", new { name = "sprint", kind = 0 });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Lists_DeletingOneKeepsItsItems_OnRealPostgres()
    {
        var s = await SceneAsync();
        var listId = await IdOf(await s.Anna.PostAsJsonAsync($"api/teams/{s.TeamId}/lists", new { name = "Sprint", kind = 0 }));
        var itemId = await AddTaskAsync(s, s.AnnaId);

        var moved = await s.Anna.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks/bulk-move", new { taskIds = new[] { itemId }, itemListId = listId });
        Assert.Equal(1, (await moved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("succeeded").GetInt32());
        var inList = await s.Anna.GetFromJsonAsync<JsonElement>($"api/tasks/{itemId}");
        Assert.Equal(listId, inList.GetProperty("task").GetProperty("itemListId").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await s.Anna.DeleteAsync($"api/teams/{s.TeamId}/lists/{listId}")).StatusCode);

        var after = await s.Anna.GetFromJsonAsync<JsonElement>($"api/tasks/{itemId}");
        Assert.Equal(JsonValueKind.Null, after.GetProperty("task").GetProperty("itemListId").ValueKind);
    }

    [Fact]
    public async Task Create_WithAListOfItsKind_AndAnUpdateThatResendsIt_KeepsTheList()
    {
        var s = await SceneAsync();
        var listId = await IdOf(await s.Anna.PostAsJsonAsync($"api/teams/{s.TeamId}/lists", new { name = "Sprint", kind = 0 }));

        var created = await s.Anna.PostAsJsonAsync($"api/teams/{s.TeamId}/tasks", new
        {
            title = "Report", impactScore = 5, assignedUserId = s.AnnaId, deadlineUtc = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-ddTHH:mm:ss'Z'"),
            itemListId = listId
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = await IdOf(created);

        // A client from before lists (no TaskType in the update) must not pull the item out of its list.
        var updated = await s.Anna.PutAsJsonAsync($"api/tasks/{id}", new
        {
            title = "Report v2", status = (int)TaskStatus.InProgress, assignedUserId = s.AnnaId,
            deadlineUtc = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-ddTHH:mm:ss'Z'"), impactScore = 5
        });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(listId, (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("itemListId").GetGuid());
    }
}
