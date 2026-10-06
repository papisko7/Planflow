namespace PlanFlow.Domain.Enums;

/// <summary>Where a notification stands on its way to the user's phone (the in-app inbox row itself is always there).</summary>
public enum PushDeliveryState
{
    /// <summary>The user does not want a push for this one (push switched off, or a category that has no push form).</summary>
    NotWanted = 0,

    /// <summary>Waiting for <c>PushDeliveryJob</c> to hand it to the push service.</summary>
    Pending = 1,

    /// <summary>Handed over (or the user has no registered phone, so there was nobody to push to).</summary>
    Sent = 2,

    /// <summary>The push service kept refusing it; the inbox entry still exists.</summary>
    Failed = 3
}
