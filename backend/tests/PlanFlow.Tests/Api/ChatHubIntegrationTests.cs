using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Chat.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Tests.Integration.PostgresIntegration;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Black-box verification of <see cref="PlanFlow.Api.Hubs.ChatHub"/> through a real SignalR client
/// talking to the in-process <see cref="CustomWebApplicationFactory"/> TestServer, exercising the
/// same JWT-over-query-string handshake (see <c>AuthenticationConfig</c>) the mobile client uses.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ChatHubIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ChatHubIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Connect_WithoutToken_Fails()
    {
        await using var connection = BuildConnection(accessToken: null);

        var act = async () => await connection.StartAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SendMessage_PersistsToDatabase_AndBroadcastsToGroup()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Member);
        var token = await MintAccessTokenAsync(userId, teamId, TeamRole.Member);

        await using var sender = BuildConnection(token);
        await using var listener = BuildConnection(token);

        var received = new TaskCompletionSource<ChatMessageDto>();
        listener.On<ChatMessageDto>("ReceiveMessage", message => received.TrySetResult(message));

        await sender.StartAsync();
        await listener.StartAsync();

        await sender.InvokeAsync("SendMessage", teamId, "Hello team");

        var broadcast = await WaitAsync(received.Task);
        broadcast.Content.Should().Be("Hello team");
        broadcast.SenderUserId.Should().Be(userId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var stored = await context.ChatMessages.AsNoTracking().SingleAsync(m => m.Id == broadcast.Id);
        stored.TeamId.Should().Be(teamId);
        stored.Content.Should().Be("Hello team");
    }

    [Fact]
    public async Task TypingIndicator_BroadcastsToOthers_ButNotToSender()
    {
        var (teamId, userId) = await SeedTeamWithMemberAsync(TeamRole.Member);
        var token = await MintAccessTokenAsync(userId, teamId, TeamRole.Member);

        await using var typist = BuildConnection(token);
        await using var observer = BuildConnection(token);

        var observerReceived = new TaskCompletionSource<bool>();
        observer.On<Guid, bool>("UserTyping", (_, isTyping) => observerReceived.TrySetResult(isTyping));

        var typistReceivedOwnEvent = false;
        typist.On<Guid, bool>("UserTyping", (_, _) => typistReceivedOwnEvent = true);

        await typist.StartAsync();
        await observer.StartAsync();

        await typist.InvokeAsync("TypingIndicator", teamId, true);

        var isTyping = await WaitAsync(observerReceived.Task);
        isTyping.Should().BeTrue();

        // Give the (absent) self-notification a moment to arrive before asserting it never does.
        await Task.Delay(200);
        typistReceivedOwnEvent.Should().BeFalse();
    }

    private HubConnection BuildConnection(string? accessToken)
    {
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/chat", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();
    }

    private static async Task<T> WaitAsync<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(task, "the expected hub event should arrive within the timeout");
        return await task;
    }

    private async Task<(Guid TeamId, Guid UserId)> SeedTeamWithMemberAsync(TeamRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid()}@example.com",
            DisplayName = "Chat Test User",
            PasswordHash = "unused-in-these-tests"
        };
        var team = new Team { Id = Guid.NewGuid(), Name = "Chat Test Team" };
        var membership = new TeamMember { Id = Guid.NewGuid(), UserId = user.Id, TeamId = team.Id, Role = role };

        context.Users.Add(user);
        context.Teams.Add(team);
        context.TeamMembers.Add(membership);
        await context.SaveChangesAsync(CancellationToken.None);

        return (team.Id, user.Id);
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
