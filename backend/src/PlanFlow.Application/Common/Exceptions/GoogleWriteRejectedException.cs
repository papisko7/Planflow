namespace PlanFlow.Application.Common.Exceptions;

/// <summary>Why Google refused to change an event or task.</summary>
public enum GoogleWriteFailure
{
    /// <summary>The account did not agree to let PlanFlow change its data (the connection predates the write permission, or it was unticked).</summary>
    MissingPermission,

    /// <summary>The event or task is gone from Google.</summary>
    Gone,

    /// <summary>Google will not take this change (an event owned by somebody else, a read-only kind of event, a value it does not accept...).</summary>
    Rejected
}

/// <summary>
/// Thrown by the Google clients when Google answers a change with a refusal that retrying cannot fix. A rejected access token is
/// <see cref="UnauthorizedException"/> and a temporary failure (rate limit, outage) is an <see cref="HttpRequestException"/>, so the sync jobs can tell
/// "ask the user to reconnect", "try again next time" and "this change will never be accepted" apart.
/// </summary>
public class GoogleWriteRejectedException : Exception
{
    public GoogleWriteRejectedException(GoogleWriteFailure failure, string message) : base(message)
    {
        Failure = failure;
    }

    public GoogleWriteFailure Failure { get; }
}
