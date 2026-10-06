using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Interfaces;
using StackExchange.Redis;

namespace PlanFlow.Infrastructure.Presence;

/// <summary>
/// Presence shared by every server instance through Redis. Each user has a sorted set whose members are connection ids and
/// whose score is "valid until" (unix seconds). A user is online while that set holds at least one unexpired member.
/// The instance that owns a connection renews its score every <see cref="HeartbeatInterval"/>, so if an instance crashes
/// without saying goodbye, its connections simply expire after <see cref="DefaultLifetime"/> instead of staying "online" forever.
/// If Redis is unreachable the answer falls back to the connections this instance knows about, so chat never breaks.
/// </summary>
public class RedisChatPresence : IChatPresence
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisChatPresence> _logger;
    private readonly TimeSpan _lifetime;

    /// <summary>The connections that belong to this instance (connection id → user id); only these are renewed by the heartbeat.</summary>
    private readonly ConcurrentDictionary<string, Guid> _local = new();

    /// <param name="lifetime">How long a connection counts as online without being renewed; tests shorten it.</param>
    public RedisChatPresence(IConnectionMultiplexer redis, ILogger<RedisChatPresence> logger, TimeSpan? lifetime = null)
    {
        _redis = redis;
        _logger = logger;
        _lifetime = lifetime ?? DefaultLifetime;
    }

    private static RedisKey KeyFor(Guid userId) => $"planflow:presence:{userId:N}";

    private double ExpiryScore() => DateTimeOffset.UtcNow.Add(_lifetime).ToUnixTimeSeconds();

    public async Task ConnectedAsync(Guid userId, string connectionId)
    {
        _local[connectionId] = userId;
        await RegisterAsync(userId, connectionId, "connect");
    }

    public async Task DisconnectedAsync(Guid userId, string connectionId)
    {
        _local.TryRemove(connectionId, out _);
        await TryAsync(db => db.SortedSetRemoveAsync(KeyFor(userId), connectionId), "disconnect");
    }

    public async Task<bool> IsOnlineAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var nowScore = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await db.SortedSetRemoveRangeByScoreAsync(KeyFor(userId), double.NegativeInfinity, nowScore); // drop expired members
            return await db.SortedSetLengthAsync(KeyFor(userId)) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis presence lookup failed; answering from the connections of this instance only");
            return _local.Values.Contains(userId);
        }
    }

    /// <summary>Renews the "valid until" of every connection owned by this instance.</summary>
    public async Task RenewAsync()
    {
        foreach (var (connectionId, userId) in _local)
        {
            await RegisterAsync(userId, connectionId, "heartbeat");
        }
    }

    private Task RegisterAsync(Guid userId, string connectionId, string what) =>
        TryAsync(async db =>
        {
            await db.SortedSetAddAsync(KeyFor(userId), connectionId, ExpiryScore());
            await db.KeyExpireAsync(KeyFor(userId), _lifetime * 2); // an abandoned key cleans itself up
        }, what);

    private async Task TryAsync(Func<IDatabase, Task> action, string what)
    {
        try
        {
            await action(_redis.GetDatabase());
        }
        catch (Exception ex)
        {
            // Presence is a convenience for the chat digest; a Redis hiccup must never fail a chat connection.
            _logger.LogWarning(ex, "Redis presence {Operation} failed", what);
        }
    }
}

/// <summary>Calls <see cref="RedisChatPresence.RenewAsync"/> on a timer for as long as the application runs.</summary>
public class ChatPresenceHeartbeat : BackgroundService
{
    private readonly RedisChatPresence _presence;

    public ChatPresenceHeartbeat(RedisChatPresence presence)
    {
        _presence = presence;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RedisChatPresence.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _presence.RenewAsync();
        }
    }
}
