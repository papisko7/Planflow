using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Integration.PostgresIntegration;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Through the real HTTP pipeline and PostgreSQL: the lock on an assignment an Owner/Admin made, and the caller's own
/// time off that the app uses to warn about a deadline that falls in it.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AssignmentLockAndAbsencesApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AssignmentLockAndAbsencesApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Scene(HttpClient Owner, HttpClient Anna, HttpClient Max, Guid TeamId, Guid OwnerId, Guid AnnaId, Guid MaxId, Guid AnnaPersonalTeamId);

    private async Task<Scene> SceneAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var team = new Team { Name = "Lock team" };
        var annaPersonal = new Team { Name = "Personal", IsPersonal = true };
        db.Teams.AddRange(team, annaPersonal);

        User NewUser(string name) => new() { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = name, PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var owner = NewUser("Owner");
        var anna = NewUser("Anna");
        var max = NewUser("Max");
        db.Users.AddRange(owner, anna, max);
        db.TeamMembers.AddRange(
            new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner },
            new TeamMember { TeamId = team.Id, UserId = anna.Id, Role = TeamRole.Member },
            new TeamMember { TeamId = team.Id, UserId = max.Id, Role = TeamRole.Member },
            new TeamMember { TeamId = annaPersonal.Id, UserId = anna.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        HttpClient ClientFor(User user, TeamRole role)
        {
            var (token, _) = jwt.GenerateAccessToken(user, role, team.Id);
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        return new Scene(
            ClientFor(owner, TeamRole.Owner), ClientFor(anna, TeamRole.Member), ClientFor(max, TeamRole.Member),
            team.Id, owner.Id, anna.Id, max.Id, annaPersonal.Id);
    }

    private static async Task<TaskDto> CreateAsync(HttpClient client, Guid teamId, Guid? assignee)
    {
        var response = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest("Report", null, assignee, null, 3, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }

    private static UpdateTaskRequest Edit(Guid? assignee, IReadOnlyList<Guid>? participants = null) =>
        new("Report", null, TaskStatus.Backlog, assignee, null, 3, null, null, null, ParticipantUserIds: participants);

    [Fact]
    public async Task OwnerAssigns_TheTaskComesBackLockedWithTheAssigner()
    {
        var s = await SceneAsync();

        var task = await CreateAsync(s.Owner, s.TeamId, s.AnnaId);

        Assert.True(task.IsAssignmentLocked);
        Assert.Equal(s.OwnerId, task.AssignedByUserId);
    }

    [Fact]
    public async Task MemberTriesToGiveBackALockedTask_Gets403WithTheReason_AndTheTaskStaysTheirs()
    {
        var s = await SceneAsync();
        var task = await CreateAsync(s.Owner, s.TeamId, s.AnnaId);

        var response = await s.Anna.PutAsJsonAsync($"api/tasks/{task.Id}", Edit(null));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("assigned to you by an Owner or Admin", JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString());
        var stored = await (await s.Anna.GetAsync($"api/tasks/{task.Id}")).Content.ReadFromJsonAsync<TaskDetailDto>();
        Assert.Equal(s.AnnaId, stored!.Task.AssignedUserId);
    }

    [Fact]
    public async Task AssigneeOfALockedTask_StillEditsItAndInvitesACoContractor()
    {
        var s = await SceneAsync();
        var task = await CreateAsync(s.Owner, s.TeamId, s.AnnaId);

        var response = await s.Anna.PutAsJsonAsync($"api/tasks/{task.Id}", Edit(s.AnnaId, new[] { s.MaxId }));
        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { s.MaxId }, updated!.ParticipantUserIds);
        Assert.True(updated.IsAssignmentLocked);
    }

    [Fact]
    public async Task OwnerReassignsALockedTask_AndTheNewAssigneeIsLockedInTurn()
    {
        var s = await SceneAsync();
        var task = await CreateAsync(s.Owner, s.TeamId, s.AnnaId);

        var response = await s.Owner.PutAsJsonAsync($"api/tasks/{task.Id}", Edit(s.MaxId));
        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(s.MaxId, updated!.AssignedUserId);
        Assert.True(updated.IsAssignmentLocked);
        // The previous assignee is out of it: Max's task is not theirs to touch.
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Anna.PutAsJsonAsync($"api/tasks/{task.Id}", Edit(null))).StatusCode);
    }

    [Fact]
    public async Task MemberGivesBackATaskTheyCreatedForThemselves_Works()
    {
        var s = await SceneAsync();
        var task = await CreateAsync(s.Anna, s.TeamId, s.AnnaId);

        Assert.False(task.IsAssignmentLocked);
        var response = await s.Anna.PutAsJsonAsync($"api/tasks/{task.Id}", Edit(null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<TaskDto>())!.AssignedUserId);
    }

    [Fact]
    public async Task MyAbsences_ListsTheCallersOwnOutOfOfficeEvenWhenTheyAreMarkedPrivate_AndNobodyElsesAbsences()
    {
        var s = await SceneAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            db.CalendarItems.AddRange(
                new CalendarItem
                {
                    TeamId = s.AnnaPersonalTeamId, AssignedUserId = s.AnnaId, Title = "Holiday", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
                    EventTemplate = EventTemplate.OutOfOffice, StartUtc = DateTime.UtcNow.AddDays(3), EndUtc = DateTime.UtcNow.AddDays(6),
                    IsPrivate = true, ExternalCalendarEventId = Guid.NewGuid().ToString()
                },
                new CalendarItem
                {
                    TeamId = s.TeamId, AssignedUserId = s.AnnaId, Title = "Conference", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
                    EventTemplate = EventTemplate.OutOfOffice, StartUtc = DateTime.UtcNow.AddDays(10), EndUtc = DateTime.UtcNow.AddDays(11)
                },
                new CalendarItem
                {
                    TeamId = s.TeamId, AssignedUserId = s.AnnaId, Title = "Meeting", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
                    StartUtc = DateTime.UtcNow.AddDays(2), EndUtc = DateTime.UtcNow.AddDays(2).AddHours(1)
                },
                new CalendarItem
                {
                    TeamId = s.TeamId, AssignedUserId = s.MaxId, Title = "Max away", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
                    EventTemplate = EventTemplate.OutOfOffice, StartUtc = DateTime.UtcNow.AddDays(2), EndUtc = DateTime.UtcNow.AddDays(4)
                });
            await db.SaveChangesAsync();
        }

        var mine = JsonDocument.Parse(await (await s.Anna.GetAsync("api/users/me/absences")).Content.ReadAsStringAsync()).RootElement;
        var others = JsonDocument.Parse(await (await s.Owner.GetAsync("api/users/me/absences")).Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(2, mine.GetArrayLength()); // the two out-of-office events, not the plain meeting and not Max's
        Assert.Equal(0, others.GetArrayLength());
    }

    [Fact]
    public async Task MyAbsences_Anonymous_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("api/users/me/absences")).StatusCode);
    }
}
