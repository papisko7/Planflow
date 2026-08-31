using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Options;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.Security;

namespace PlanFlow.Infrastructure.ExternalServices.Google;

/// <summary>
/// Raw HttpClient calls to Google's OAuth2/OpenID endpoints — no Google.Apis SDK dependency,
/// since the flow only needs three well-documented REST calls (authorize/token/userinfo) and
/// keeping the dependency surface small matters for a thesis defense walkthrough (Phase 4A).
/// </summary>
public class GoogleOAuthClient : IGoogleOAuthClient
{
    private readonly HttpClient _httpClient;
    private readonly GoogleOAuthOptions _options;

    public GoogleOAuthClient(HttpClient httpClient, IOptions<GoogleOAuthOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = _options.ClientId;
        query["redirect_uri"] = _options.RedirectUri;
        query["response_type"] = "code";
        query["scope"] = _options.Scopes;
        query["access_type"] = "offline"; // required for Google to issue a refresh_token
        query["prompt"] = "consent";      // forces refresh_token on every connect, not just the first
        query["state"] = state;
        query["code_challenge"] = codeChallenge;
        query["code_challenge_method"] = "S256";

        return $"{_options.AuthorizationEndpoint}?{query}";
    }

    public async Task<GoogleTokenResult> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = _options.RedirectUri
        };

        return await PostTokenRequestAsync(form, cancellationToken);
    }

    public async Task<GoogleTokenResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };

        return await PostTokenRequestAsync(form, cancellationToken);
    }

    public async Task<GoogleUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.UserInfoEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new UnauthorizedException("Google rejected the access token while fetching account info.");
        }

        var body = await response.Content.ReadFromJsonAsync<GoogleUserInfoResponse>(cancellationToken)
            ?? throw new UnauthorizedException("Google returned an empty userinfo response.");

        return new GoogleUserInfo(body.Sub, body.Email);
    }

    private async Task<GoogleTokenResult> PostTokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(_options.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Covers a denied/expired/replayed code, a PKCE mismatch, or a revoked refresh token —
            // all caller errors from PlanFlow's perspective, so map to 401 rather than a 500.
            throw new UnauthorizedException("Google rejected the OAuth token request (denied, expired, or already used).");
        }

        var body = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken)
            ?? throw new UnauthorizedException("Google returned an empty token response.");

        return new GoogleTokenResult(body.AccessToken, body.RefreshToken, body.ExpiresIn, body.Scope ?? string.Empty);
    }

    private record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("token_type")] string? TokenType);

    private record GoogleUserInfoResponse(
        [property: JsonPropertyName("sub")] string Sub,
        [property: JsonPropertyName("email")] string Email);
}
