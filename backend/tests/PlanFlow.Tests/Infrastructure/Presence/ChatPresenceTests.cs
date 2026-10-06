using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure;
using PlanFlow.Infrastructure.Presence;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace PlanFlow.Tests.Infrastructure.Presence;

/// <summary>
/// "Who is online" must be the same answer on every server instance. These tests run two <see cref="RedisChatPresence"/>
/// objects (= two server instances) against one real Redis in a throwaway container.
/// </summary>
public class ChatPresenceTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private IConnectionMultiplexer _connection = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _connection.Dispose();
        await _redis.DisposeAsync();
    }

    private RedisChatPresence Instance(TimeSpan? lifetime = null) =>
        new(_connection, NullLogger<RedisChatPresence>.Instance, lifetime);

    [Fact]
    public async Task OneInstanceSeesTheConnectionsOfAnother()
    {
        var instanceA = Instance();
        var instanceB = Instance();
        var user = Guid.NewGuid();

        Assert.False(await instanceB.IsOnlineAsync(user));

        await instanceA.ConnectedAsync(user, "conn-1");
        Assert.True(await instanceB.IsOnlineAsync(user));

        await instanceA.DisconnectedAsync(user, "conn-1");
        Assert.False(await instanceB.IsOnlineAsync(user));
    }

    [Fact]
    public async Task UserOnTwoDevices_StaysOnlineUntilTheLastOneLeaves()
    {
        var instanceA = Instance();
        var instanceB = Instance();
        var user = Guid.NewGuid();

        await instanceA.ConnectedAsync(user, "phone");
        await instanceB.ConnectedAsync(user, "tablet");

        await instanceA.DisconnectedAsync(user, "phone");
        Assert.True(await instanceA.IsOnlineAsync(user));

        await instanceB.DisconnectedAsync(user, "tablet");
        Assert.False(await instanceA.IsOnlineAsync(user));
    }

    [Fact]
    public async Task ConnectionsOfACrashedInstance_ExpireInsteadOfStayingOnlineForever()
    {
        var crashing = Instance(lifetime: TimeSpan.FromSeconds(1));
        var survivor = Instance();
        var user = Guid.NewGuid();

        await crashing.ConnectedAsync(user, "conn-1"); // never disconnected and never renewed: the instance "died"
        Assert.True(await survivor.IsOnlineAsync(user));

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        Assert.False(await survivor.IsOnlineAsync(user));
    }

    [Fact]
    public async Task RenewingKeepsALiveConnectionOnline()
    {
        var owner = Instance(lifetime: TimeSpan.FromSeconds(6));
        var observer = Instance();
        var user = Guid.NewGuid();

        await owner.ConnectedAsync(user, "conn-1");
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        await owner.RenewAsync(); // what the 30-second heartbeat does

        await Task.Delay(TimeSpan.FromSeconds(4)); // past the original expiry (6 s), inside the renewed one

        Assert.True(await observer.IsOnlineAsync(user));
    }

    [Fact]
    public async Task RedisUnreachable_FallsBackToOwnConnections_AndNeverThrows()
    {
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false, ConnectTimeout = 300, AsyncTimeout = 300, SyncTimeout = 300
        };
        options.EndPoints.Add("127.0.0.1", 1); // nothing listens here
        using var deadConnection = ConnectionMultiplexer.Connect(options);
        var presence = new RedisChatPresence(deadConnection, NullLogger<RedisChatPresence>.Instance);
        var user = Guid.NewGuid();

        await presence.ConnectedAsync(user, "conn-1");

        Assert.True(await presence.IsOnlineAsync(user));
        Assert.False(await presence.IsOnlineAsync(Guid.NewGuid()));

        await presence.DisconnectedAsync(user, "conn-1");
        Assert.False(await presence.IsOnlineAsync(user));
    }

    private static ServiceProvider Services(string environment, string? redisConnectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = redisConnectionString })
            .Build();
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(environment);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration, host.Object);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void NormalRun_WithRedisConfigured_UsesRedisPresence()
    {
        using var provider = Services("Development", _redis.GetConnectionString());

        Assert.IsType<RedisChatPresence>(provider.GetRequiredService<IChatPresence>());
    }

    [Fact]
    public void NormalRun_WithoutRedis_FallsBackToInMemoryPresence()
    {
        using var provider = Services("Development", null);

        Assert.IsType<InMemoryChatPresence>(provider.GetRequiredService<IChatPresence>());
    }

    [Fact]
    public async Task InMemoryPresence_TracksPerConnection()
    {
        var presence = new InMemoryChatPresence();
        var user = Guid.NewGuid();

        await presence.ConnectedAsync(user, "a");
        await presence.ConnectedAsync(user, "b");
        await presence.DisconnectedAsync(user, "a");
        Assert.True(await presence.IsOnlineAsync(user));

        await presence.DisconnectedAsync(user, "b");
        Assert.False(await presence.IsOnlineAsync(user));
    }
}
