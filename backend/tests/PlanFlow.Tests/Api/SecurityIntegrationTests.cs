using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Tests.Integration.PostgresIntegration;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Black-box verification of Phase 4B's security controls, exercised through the real ASP.NET Core
/// pipeline (JWT authentication, RBAC policies, rate limiting) via <see cref="CustomWebApplicationFactory"/>
/// instead of unit-testing each piece in isolation.
/// </summary>
/// <remarks>
/// Shares <see cref="PostgresCollection"/> with the Testcontainers-backed tests purely to force xUnit
/// to run them sequentially rather than in parallel: two <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// hosts of the same <c>Program</c> booting concurrently race on Quartz's static logging bridge
/// (<c>Quartz.Logging.LogProvider</c>), which throws <see cref="ObjectDisposedException"/> on
/// "LoggerFactory" when one host's provider gets disposed mid-startup of the other. This fixture
/// (<see cref="CustomWebApplicationFactory"/>) is unrelated to <see cref="PostgresWebApplicationFactory"/>
/// and still resolved independently via <see cref="IClassFixture{TFixture}"/> below.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class SecurityIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SecurityIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"api/teams/{Guid.NewGuid()}/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMalformedToken_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        var response = await client.GetAsync($"api/teams/{Guid.NewGuid()}/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTask_AsGuestRole_Returns403()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Guest);
        var token = await MintAccessTokenAsync(userId, teamId, TeamRole.Guest);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync($"api/teams/{teamId}/tasks", new
        {
            title = "Should be blocked",
            description = (string?)null,
            assignedUserId = (Guid?)null,
            deadlineUtc = (DateTime?)null,
            impactScore = 5,
            blockedByTaskId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTask_FromAnotherTeam_Returns403()
    {
        var (teamAId, userA) = await SeedTeamWithMemberAsync(TeamRole.Member);
        var (teamBId, _) = await SeedTeamWithMemberAsync(TeamRole.Member);
        var taskInTeamB = await SeedTaskAsync(teamBId);

        var tokenForTeamA = await MintAccessTokenAsync(userA, teamAId, TeamRole.Member);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenForTeamA);

        var response = await client.GetAsync($"api/tasks/{taskInTeamB}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetScoreHistory_FromAnotherTeam_Returns403()
    {
        var (teamAId, userA) = await SeedTeamWithMemberAsync(TeamRole.Member);
        var (teamBId, _) = await SeedTeamWithMemberAsync(TeamRole.Member);
        var taskInTeamB = await SeedTaskAsync(teamBId);
        var client = await ClientForAsync(userA, teamAId, TeamRole.Member);

        var response = await client.GetAsync($"api/tasks/{taskInTeamB}/score-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetScoreHistory_OwnTeam_Returns200WithLoggedEntries()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Member);
        var taskId = await SeedTaskAsync(teamId);
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            context.UrgencyScoreLogs.Add(new UrgencyScoreLog { TaskItemId = taskId, FinalScore = 0.5, TriggerSource = ScoreTriggerSource.ManualCreate });
            await context.SaveChangesAsync(CancellationToken.None);
        }
        var client = await ClientForAsync(userId, teamId, TeamRole.Member);

        var response = await client.GetAsync($"api/tasks/{taskId}/score-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("triggerSource", body);
    }

    [Fact]
    public async Task GetTeamMembers_AsMember_Returns200()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Guest);
        var client = await ClientForAsync(userId, teamId, TeamRole.Guest);

        var response = await client.GetAsync($"api/teams/{teamId}/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTeamMembers_AsNonMember_Returns403()
    {
        var (teamAId, userA) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var (teamBId, _) = await SeedTeamWithMemberAsync(TeamRole.Owner);
        var client = await ClientForAsync(userA, teamAId, TeamRole.Owner);

        var response = await client.GetAsync($"api/teams/{teamBId}/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> ClientForAsync(Guid userId, Guid teamId, TeamRole role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await MintAccessTokenAsync(userId, teamId, role));
        return client;
    }

    [Fact]
    public async Task LoginEndpoint_ExceedingRateLimit_Returns429()
    {
        // A fresh client per iteration keeps the same underlying TestServer connection (and thus
        // the same rate-limit IP partition), so hammering /api/auth/login here proves the "auth"
        // policy's 5-requests-per-minute ceiling actually rejects the 6th attempt.
        var client = _factory.CreateClient();
        HttpResponseMessage? lastResponse = null;

        for (var i = 0; i < 6; i++)
        {
            lastResponse = await client.PostAsJsonAsync("api/auth/login", new
            {
                email = "nobody@example.com",
                password = "wrong-password",
                teamId = (Guid?)null
            });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
    }

    private async Task<(Guid TeamId, Guid UserId)> SeedTeamWithMemberAsync(TeamRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid()}@example.com",
            DisplayName = "Test User",
            PasswordHash = "unused-in-these-tests"
        };
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team" };
        var membership = new TeamMember { Id = Guid.NewGuid(), UserId = user.Id, TeamId = team.Id, Role = role };

        context.Users.Add(user);
        context.Teams.Add(team);
        context.TeamMembers.Add(membership);
        await context.SaveChangesAsync(CancellationToken.None);

        return (team.Id, user.Id);
    }

    private async Task<Guid> SeedTaskAsync(Guid teamId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            Title = "Cross-team task",
            ImpactScore = 1
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync(CancellationToken.None);

        return task.Id;
    }

    private async Task<string> MintAccessTokenAsync(Guid userId, Guid teamId, TeamRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var user = await context.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var (accessToken, _) = jwtTokenService.GenerateAccessToken(user, role, teamId);
        return accessToken;
    }
}
