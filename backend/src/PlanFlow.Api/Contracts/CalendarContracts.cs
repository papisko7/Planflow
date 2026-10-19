namespace PlanFlow.Api.Contracts;

public record GoogleConnectResponse(string AuthorizationUrl);

/// <summary><paramref name="Started"/> is false when the last sync was moments ago and nothing was started.</summary>
public record GoogleSyncResponse(bool Started);

/// <summary>Bound from the query string on Google's redirect; <paramref name="Error"/> is set instead of Code/State when the user denies consent.</summary>
public record GoogleCallbackRequest(string? Code, string? State, string? Error);
