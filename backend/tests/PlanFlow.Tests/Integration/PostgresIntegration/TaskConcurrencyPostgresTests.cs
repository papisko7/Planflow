using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Api.Contracts;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// Verifies the task-update path survives real concurrent writes against a real PostgreSQL
/// connection pool — something the InMemory provider can't meaningfully test, since it has no
/// actual connection contention or transaction isolation.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskConcurrencyPostgresTests
{
    private readonly PostgresWebApplicationFactory _factory;

    public TaskConcurrencyPostgresTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentStatusUpdates_OnSameTask_AllSucceedAndEveryChangeIsAudited()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync();
        var client = await AuthenticatedClientAsync(userId, teamId);

        var createResponse = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
            "Contended task", null, null, null, 4, null));
        var created = await createResponse.Content.ReadFromJsonAsync<TaskDto>();

        // Fire N concurrent PUTs at the same task row from independent HttpClients (independent
        // connections from the pool) to prove the update handler doesn't deadlock, crash, or lose
        // writes under real concurrent database access.
        const int concurrentRequests = 10;
        var statuses = new[] { TaskStatus.InProgress, TaskStatus.Blocked, TaskStatus.Done };

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
        {
            var requestClient = await AuthenticatedClientAsync(userId, teamId);
            return await requestClient.PutAsJsonAsync($"api/tasks/{created!.Id}", new UpdateTaskRequest(
                "Contended task", null, statuses[i % statuses.Length], null, null, 4, null, null, null));
        });

        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var finalTask = await context.Tasks.AsNoTracking().SingleAsync(t => t.Id == created!.Id);
        statuses.Should().Contain(finalTask.Status);

        // Every request must have landed as its own audit row: no writes silently dropped.
        var historyCount = await context.TaskHistories.AsNoTracking().CountAsync(h => h.TaskItemId == created!.Id);
        historyCount.Should().Be(concurrentRequests + 1); // +1 for the initial Created entry.

        var scoreLogCount = await context.UrgencyScoreLogs.AsNoTracking().CountAsync(l => l.TaskItemId == created!.Id);
        scoreLogCount.Should().Be(concurrentRequests + 1); // +1 for the initial ManualCreate log.
    }

    [Fact]
    public async Task ConcurrentTaskCreation_ForSameTeam_AllPersistWithoutCollision()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync();

        const int concurrentRequests = 15;
        var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
        {
            var client = await AuthenticatedClientAsync(userId, teamId);
            return await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new CreateTaskRequest(
                $"Task {i}", null, null, null, i % 10, null));
        });

        var responses = await Task.WhenAll(tasks);
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var count = await context.Tasks.AsNoTracking().CountAsync(t => t.TeamId == teamId);

        count.Should().Be(concurrentRequests);
    }

    private async Task<(Guid TeamId, Guid UserId)> SeedTeamWithMemberAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var team = new Team { Id = Guid.NewGuid(), Name = "Concurrency Team" };
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid()}@example.com",
            DisplayName = "Concurrency User",
            PasswordHash = "unused-in-these-tests"
        };

        context.Teams.Add(team);
        context.Users.Add(user);
        context.TeamMembers.Add(new TeamMember { Id = Guid.NewGuid(), UserId = user.Id, TeamId = team.Id, Role = TeamRole.Owner });

        await context.SaveChangesAsync(CancellationToken.None);
        return (team.Id, user.Id);
    }

    private async Task<HttpClient> AuthenticatedClientAsync(Guid userId, Guid teamId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var user = await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        var (accessToken, _) = jwtTokenService.GenerateAccessToken(user, TeamRole.Owner, teamId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
