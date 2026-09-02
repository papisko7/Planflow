using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// End-to-end HTTP tests for the task CRUD + team-grouping + audit-trail endpoints, run against a
/// real PostgreSQL instance (see <see cref="PostgresWebApplicationFactory"/>) instead of the InMemory
/// provider used elsewhere, so real SQL translation (ordering, FK cascade deletes, indexes) is
/// actually exercised rather than assumed.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskCrudPostgresTests
{
    private readonly PostgresWebApplicationFactory _factory;

    public TaskCrudPostgresTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateTask_PersistsToRealDatabase_WithComputedUrgencyScoreAndAuditEntry()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var client = await AuthenticatedClientAsync(userId, teamId, TeamRole.Owner);

        var response = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "Ship the report",
            "Quarterly report for the board",
            null,
            DateTime.UtcNow.AddDays(2),
            8,
            null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TaskDto>();
        created.Should().NotBeNull();
        created!.CurrentUrgencyScore.Should().BeInRange(0.0, 1.0);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var stored = await context.Tasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        stored.Title.Should().Be("Ship the report");
        stored.CurrentUrgencyScore.Should().Be(created.CurrentUrgencyScore);

        var history = await context.TaskHistories.AsNoTracking().Where(h => h.TaskItemId == created.Id).ToListAsync();
        history.Should().ContainSingle(h => h.ChangeType == TaskChangeType.Created);

        var scoreLogs = await context.UrgencyScoreLogs.AsNoTracking().Where(l => l.TaskItemId == created.Id).ToListAsync();
        scoreLogs.Should().ContainSingle(l => l.TriggerSource == ScoreTriggerSource.ManualCreate);
    }

    [Fact]
    public async Task GetTeamTasks_GroupsTasksByTeam_OrderedByUrgencyDescending()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var client = await AuthenticatedClientAsync(userId, teamId, TeamRole.Owner);

        // Low urgency: far deadline, low impact.
        await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "Low urgency", null, null, DateTime.UtcNow.AddDays(29), 1, null));

        // High urgency: max impact now, pushed overdue via an update afterwards — CreateTask's
        // validator rejects a past DeadlineUtc outright (deadlines can only be set in the future),
        // while UpdateTask has no such restriction.
        var highUrgencyResponse = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "High urgency", null, null, DateTime.UtcNow.AddDays(1), 10, null));
        var highUrgency = await highUrgencyResponse.Content.ReadFromJsonAsync<TaskDto>();
        await client.PutAsJsonAsync($"api/tasks/{highUrgency!.Id}", new UpdateTaskRequest(
            "High urgency", null, TaskStatus.Todo, null, DateTime.UtcNow.AddDays(-1), 10, null, null, null));

        var (otherTeamId, otherUserId) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var otherClient = await AuthenticatedClientAsync(otherUserId, otherTeamId, TeamRole.Owner);
        await otherClient.PostAsJsonAsync($"api/teams/{otherTeamId}/tasks", new CreateTaskRequest(
            "Other team's task", null, null, null, 10, null));

        var response = await client.GetAsync($"api/teams/{teamId}/tasks");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = await response.Content.ReadFromJsonAsync<List<TaskDto>>();
        tasks.Should().NotBeNull();
        tasks!.Should().HaveCount(2);
        tasks!.Should().OnlyContain(t => t.TeamId == teamId);
        tasks![0].Title.Should().Be("High urgency");
        tasks![0].CurrentUrgencyScore.Should().BeGreaterThan(tasks![1].CurrentUrgencyScore);
    }

    [Fact]
    public async Task UpdateTask_StatusChange_WritesAuditEntryAndRecalculatesScore()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var client = await AuthenticatedClientAsync(userId, teamId, TeamRole.Owner);

        var createResponse = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "Draft task", null, null, null, 3, null));
        var created = await createResponse.Content.ReadFromJsonAsync<TaskDto>();

        var updateResponse = await client.PutAsJsonAsync($"api/tasks/{created!.Id}", new UpdateTaskRequest(
            "Draft task",
            "Now in progress",
            TaskStatus.InProgress,
            null,
            null,
            3,
            null,
            null,
            null));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<TaskDto>();
        updated!.Status.Should().Be(TaskStatus.InProgress);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var history = await context.TaskHistories.AsNoTracking()
            .Where(h => h.TaskItemId == created.Id)
            .OrderBy(h => h.CreatedAtUtc)
            .ToListAsync();

        history.Should().HaveCount(2);
        history[0].ChangeType.Should().Be(TaskChangeType.Created);
        history[1].ChangeType.Should().Be(TaskChangeType.StatusChanged);
    }

    [Fact]
    public async Task UpdateTask_Reassignment_WritesReassignedAuditEntry()
    {
        var (teamId, ownerId) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var (_, assigneeId) = await SeedTeamWithMemberAsync(TeamRole.Owner, existingTeamId: teamId);
        var client = await AuthenticatedClientAsync(ownerId, teamId, TeamRole.Owner);

        var createResponse = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "Unassigned task", null, null, null, 2, null));
        var created = await createResponse.Content.ReadFromJsonAsync<TaskDto>();

        var updateResponse = await client.PutAsJsonAsync($"api/tasks/{created!.Id}", new UpdateTaskRequest(
            "Unassigned task", null, TaskStatus.Todo, assigneeId, null, 2, null, null, null));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var history = await context.TaskHistories.AsNoTracking()
            .Where(h => h.TaskItemId == created.Id)
            .OrderBy(h => h.CreatedAtUtc)
            .ToListAsync();

        history.Should().HaveCount(2);
        history[1].ChangeType.Should().Be(TaskChangeType.Reassigned);
    }

    [Fact]
    public async Task UpdateTask_BlockingRelationship_RaisesBlockingTaskUrgencyOnUpdate()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var client = await AuthenticatedClientAsync(userId, teamId, TeamRole.Owner);

        var blockerResponse = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "Blocker", null, null, null, 0, null));
        var blocker = await blockerResponse.Content.ReadFromJsonAsync<TaskDto>();
        var blockerInitialScore = blocker!.CurrentUrgencyScore;

        var blockedResponse = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "Blocked", null, null, null, 0, blocker.Id));
        var blocked = await blockedResponse.Content.ReadFromJsonAsync<TaskDto>();

        // Touch the blocker itself so its score is recomputed with the now-existing dependent task counted.
        var updateResponse = await client.PutAsJsonAsync($"api/tasks/{blocker.Id}", new UpdateTaskRequest(
            "Blocker", null, TaskStatus.Todo, null, null, 0, null, null, null));
        var updatedBlocker = await updateResponse.Content.ReadFromJsonAsync<TaskDto>();

        updatedBlocker!.CurrentUrgencyScore.Should().BeGreaterThan(blockerInitialScore);
        blocked!.BlockedByTaskId.Should().Be(blocker.Id);
    }

    [Fact]
    public async Task GetTaskDetail_ReturnsUrgencyBreakdown_FromLatestScoreLog()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var client = await AuthenticatedClientAsync(userId, teamId, TeamRole.Owner);

        var createResponse = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "Detail me", null, null, DateTime.UtcNow.AddDays(1), 10, null));
        var created = await createResponse.Content.ReadFromJsonAsync<TaskDto>();

        // Push the deadline overdue via UpdateTask — CreateTask's validator rejects a past
        // DeadlineUtc outright, while UpdateTask allows it (see CreateTaskCommandValidator).
        await client.PutAsJsonAsync($"api/tasks/{created!.Id}", new UpdateTaskRequest(
            "Detail me", null, TaskStatus.Todo, null, DateTime.UtcNow.AddDays(-1), 10, null, null, null));

        var response = await client.GetAsync($"api/tasks/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await response.Content.ReadFromJsonAsync<TaskDetailDto>();
        detail.Should().NotBeNull();
        detail!.LatestScoreBreakdown.Should().NotBeNull();
        detail.LatestScoreBreakdown!.DeadlineComponent.Should().Be(1.0);
        detail.LatestScoreBreakdown.ImpactComponent.Should().Be(1.0);
    }

    [Fact]
    public async Task DeleteTask_CascadeDeletesHistoryAndScoreLogs_ViaRealForeignKeys()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var client = await AuthenticatedClientAsync(userId, teamId, TeamRole.Owner);

        var createResponse = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "Temporary task", null, null, null, 1, null));
        var created = await createResponse.Content.ReadFromJsonAsync<TaskDto>();

        await client.PutAsJsonAsync($"api/tasks/{created!.Id}", new UpdateTaskRequest(
            "Temporary task", null, TaskStatus.InProgress, null, null, 1, null, null, null));

        var deleteResponse = await client.DeleteAsync($"api/tasks/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        (await context.Tasks.AsNoTracking().AnyAsync(t => t.Id == created.Id)).Should().BeFalse();
        (await context.TaskHistories.AsNoTracking().AnyAsync(h => h.TaskItemId == created.Id)).Should().BeFalse();
        (await context.UrgencyScoreLogs.AsNoTracking().AnyAsync(l => l.TaskItemId == created.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task GetTask_NotFound_Returns404()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var client = await AuthenticatedClientAsync(userId, teamId, TeamRole.Owner);

        var response = await client.GetAsync($"api/tasks/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(Guid TeamId, Guid UserId)> SeedTeamWithMemberAsync(TeamRole role, Guid? existingTeamId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var teamId = existingTeamId ?? Guid.NewGuid();
        if (existingTeamId is null)
        {
            context.Teams.Add(new Team { Id = teamId, Name = "Test Team" });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid()}@example.com",
            DisplayName = "Test User",
            PasswordHash = "unused-in-these-tests"
        };
        context.Users.Add(user);
        context.TeamMembers.Add(new TeamMember { Id = Guid.NewGuid(), UserId = user.Id, TeamId = teamId, Role = role });

        await context.SaveChangesAsync(CancellationToken.None);
        return (teamId, user.Id);
    }

    private async Task<HttpClient> AuthenticatedClientAsync(Guid userId, Guid teamId, TeamRole role)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var user = await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        var (accessToken, _) = jwtTokenService.GenerateAccessToken(user, role, teamId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
