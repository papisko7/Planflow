namespace PlanFlow.Api.Contracts;

/// <summary><paramref name="Token"/> is the Expo push token of this phone; <paramref name="Platform"/> is "ios" or "android".</summary>
public record RegisterPushTokenRequest(string Token, string Platform);

/// <summary>What the bell shows.</summary>
public record UnreadCountResponse(int Count);
