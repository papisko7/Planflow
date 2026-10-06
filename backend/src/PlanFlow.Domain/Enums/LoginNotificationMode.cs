namespace PlanFlow.Domain.Enums;

/// <summary>When to e-mail the user about a sign-in to their account.</summary>
public enum LoginNotificationMode
{
    /// <summary>Only when the sign-in comes from a device PlanFlow has not seen before (default — an e-mail per login would be spam).</summary>
    NewDevice = 0,
    EveryLogin = 1,
    Off = 2
}
