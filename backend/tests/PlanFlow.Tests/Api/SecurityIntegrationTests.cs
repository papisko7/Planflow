using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Black-box verification of Phase 4B's security controls, exercised through the real ASP.NET Core
/// pipeline (JWT authentication, RBAC policies, rate limiting) via <see cref="CustomWebApplicationFactory"/>
/// instead of unit-testing each piece in isolation.
/// </summary>
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
