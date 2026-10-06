namespace PlanFlow.Application.Common.Interfaces;

/// <summary>One push to one phone. <paramref name="Data"/> travels with it so the app knows which screen to open on tap.</summary>
public record PushMessage(string To, string Title, string Body, int Badge, IReadOnlyDictionary<string, string> Data);

/// <summary>The push service's verdict for one <see cref="PushMessage"/>.</summary>
/// <param name="Accepted">The service took the message (delivery to the phone is then up to Apple/Google).</param>
/// <param name="TokenInvalid">The phone is gone (app uninstalled, token revoked) — the token must be forgotten.</param>
/// <param name="Error">Why it was refused, for the log.</param>
public record PushResult(bool Accepted, bool TokenInvalid, string? Error);

/// <summary>Hands pushes to the push service. Only <c>PushDeliveryJob</c> calls this, never a request handler.</summary>
public interface IPushSender
{
    /// <summary>Returns one result per message, in the same order. Throws when the push service itself cannot be reached.</summary>
    Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken);
}
