namespace PlanFlow.Api.Contracts;

/// <summary><paramref name="Token"/> is the Expo push token of this phone; <paramref name="Platform"/> is "ios" or "android".</summary>
public record RegisterPushTokenRequest(string Token, string Platform);

/// <summary>The entries ticked in the inbox, for "mark read" / "delete" on several at once.</summary>
public record NotificationIdsRequest(List<Guid> Ids);

/// <summary>What the bell shows.</summary>
public record UnreadCountResponse(int Count);
