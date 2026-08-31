namespace PlanFlow.Application.Common.Interfaces;

/// <summary>Google's token-endpoint response (snake_case fields per the OAuth2 spec, mapped in the Infrastructure implementation).</summary>
public record GoogleTokenResult(
    string AccessToken,
    string? RefreshToken,
    int ExpiresInSeconds,
    string Scope);

/// <summary>Subset of Google's OpenID Connect userinfo response needed to key <see cref="PlanFlow.Domain.Entities.CalendarIntegration"/>.</summary>
public record GoogleUserInfo(string Sub, string Email);

/// <summary>
/// Talks to Google's OAuth2/OpenID endpoints. Declared here so Application-layer command handlers
/// depend on an abstraction (and can be unit-tested with a fake) instead of an HttpClient directly;
/// implemented in Infrastructure (Phase 4A).
/// </summary>
public interface IGoogleOAuthClient
{
    /// <summary>Builds the Google consent-screen URL for an Authorization Code + PKCE (S256) flow.</summary>
    string BuildAuthorizationUrl(string state, string codeChallenge);

    /// <summary>Exchanges a consent-screen authorization code (plus the PKCE verifier) for an access/refresh token pair.</summary>
    Task<GoogleTokenResult> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken);

    /// <summary>Exchanges a stored refresh token for a new access token. Google does not reissue a refresh token here.</summary>
    Task<GoogleTokenResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task<GoogleUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken);
}
