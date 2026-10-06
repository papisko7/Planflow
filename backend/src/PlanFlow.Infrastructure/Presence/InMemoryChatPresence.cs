using System.Collections.Concurrent;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Infrastructure.Presence;

/// <summary>
/// Presence kept in this process only. Used when Redis is not configured (and in tests): correct for a single server
/// instance, but another instance cannot see these connections — that is what <see cref="RedisChatPresence"/> is for.
/// </summary>
public class InMemoryChatPresence : IChatPresence
{
    private readonly ConcurrentDictionary<string, Guid> _connections = new();

    public Task ConnectedAsync(Guid userId, string connectionId)
    {
        _connections[connectionId] = userId;
        return Task.CompletedTask;
    }

    public Task DisconnectedAsync(Guid userId, string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> IsOnlineAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_connections.Values.Contains(userId));
}
