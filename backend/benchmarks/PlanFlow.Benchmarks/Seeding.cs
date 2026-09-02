using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Benchmarks;

/// <summary>
/// Synthetic data generation shared by the API-latency and batch-job scenarios. Follows the same
/// seeding shape as <c>PlanFlow.Tests/Api/SecurityIntegrationTests.cs</c> (fresh Guids per entity,
/// Owner role for full permissions) so benchmark numbers reflect the same code paths as the test suite.
/// </summary>
public static class Seeding
{
    private static readonly Random Rng = new(42);

    public static async Task<(Guid TeamId, Guid UserId)> SeedTeamWithMemberAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid()}@example.com",
            DisplayName = "Benchmark User",
            PasswordHash = "unused-in-benchmarks"
        };
        var team = new Team { Id = Guid.NewGuid(), Name = "Benchmark Team" };
        var membership = new TeamMember { Id = Guid.NewGuid(), UserId = user.Id, TeamId = team.Id, Role = TeamRole.Owner };

        context.Users.Add(user);
        context.Teams.Add(team);
        context.TeamMembers.Add(membership);
        await context.SaveChangesAsync(CancellationToken.None);

        return (team.Id, user.Id);
    }

    /// <summary>
    /// Bulk-inserts <paramref name="count"/> tasks for <paramref name="teamId"/> with randomized
    /// deadlines/impact/blocking chains so the urgency formula's components all see realistic spread,
    /// rather than every task hashing to the same score. Uses <c>AddRange</c> + a single
    /// <c>SaveChangesAsync</c> per batch (of 1,000) to keep 10k-row seeding fast without starving
    /// the DB connection pool.
    /// </summary>
    public static async Task SeedTasksAsync(IServiceProvider services, Guid teamId, int count)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var statuses = new[] { TaskStatus.Todo, TaskStatus.InProgress, TaskStatus.Blocked };
        var previousIds = new List<Guid>();
        const int batchSize = 1000;

        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            var task = new TaskItem
            {
                Id = id,
                TeamId = teamId,
                Title = $"Benchmark task {i}",
                Status = statuses[Rng.Next(statuses.Length)],
                DeadlineUtc = DateTime.UtcNow.AddDays(Rng.Next(-5, 45)),
                ImpactScore = Rng.Next(0, 11),
                // ~10% of tasks block a previously-seeded task, giving the "blocking" urgency
                // component non-zero, non-uniform values instead of always evaluating to 0.
                BlockedByTaskId = previousIds.Count > 0 && Rng.NextDouble() < 0.1
                    ? previousIds[Rng.Next(previousIds.Count)]
                    : null,
                CurrentUrgencyScore = 0.0
            };

            context.Tasks.Add(task);
            previousIds.Add(id);

            if ((i + 1) % batchSize == 0 || i == count - 1)
            {
                await context.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    public static async Task<string> MintAccessTokenAsync(IServiceProvider services, Guid userId, Guid teamId)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var user = await context.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var (accessToken, _) = jwtTokenService.GenerateAccessToken(user, TeamRole.Owner, teamId);
        return accessToken;
    }

    /// <summary>
    /// Adds <paramref name="count"/> more members to <paramref name="teamId"/> and mints one access
    /// token per user. A real 100-req/sec load scenario is 100 concurrent real users, not one user
    /// hammering the API — and <see cref="PlanFlow.Api.RateLimitingConfig"/>'s global limiter is
    /// partitioned per authenticated user (100 requests/minute each), so reusing a single token
    /// across the whole load test would measure the rate limiter, not the endpoint.
    /// </summary>
    public static async Task<List<string>> SeedUsersAndMintTokensAsync(IServiceProvider services, Guid teamId, int count)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var users = new List<User>();
        for (var i = 0; i < count; i++)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = $"{Guid.NewGuid()}@example.com",
                DisplayName = $"Benchmark Load User {i}",
                PasswordHash = "unused-in-benchmarks"
            };
            users.Add(user);
            context.Users.Add(user);
            context.TeamMembers.Add(new TeamMember { Id = Guid.NewGuid(), UserId = user.Id, TeamId = teamId, Role = TeamRole.Owner });
        }

        await context.SaveChangesAsync(CancellationToken.None);

        return users.Select(u => jwtTokenService.GenerateAccessToken(u, TeamRole.Owner, teamId).AccessToken).ToList();
    }
}
