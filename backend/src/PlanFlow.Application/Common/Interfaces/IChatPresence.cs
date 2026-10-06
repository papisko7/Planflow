namespace PlanFlow.Application.Common.Interfaces;

/// <summary>
/// Whether a user currently has a live chat connection (SignalR) on any server instance. Used so the chat e-mail digest
/// only reaches people who are away. Tracked per connection id, because one user can be connected from several devices.
/// </summary>
public interface IChatPresence
{
    Task ConnectedAsync(Guid userId, string connectionId);

    Task DisconnectedAsync(Guid userId, string connectionId);

    Task<bool> IsOnlineAsync(Guid userId, CancellationToken cancellationToken = default);
}
