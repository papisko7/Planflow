using System.Collections.Concurrent;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Infrastructure.Push;

/// <summary>Keeps pushes in memory instead of sending them. Registered only under the test environments so nothing leaves the machine.</summary>
public class InMemoryPushSender : IPushSender
{
    private readonly ConcurrentQueue<PushMessage> _sent = new();

    public IReadOnlyCollection<PushMessage> Sent => _sent.ToArray();

    public Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            _sent.Enqueue(message);
        }

        return Task.FromResult<IReadOnlyList<PushResult>>(messages.Select(_ => new PushResult(true, false, null)).ToList());
    }
}
