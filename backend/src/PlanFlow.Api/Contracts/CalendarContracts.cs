namespace PlanFlow.Api.Contracts;

public record GoogleConnectResponse(string AuthorizationUrl);

/// <summary>Bound from the query string on Google's redirect; <paramref name="Error"/> is set instead of Code/State when the user denies consent.</summary>
public record GoogleCallbackRequest(string? Code, string? State, string? Error);
