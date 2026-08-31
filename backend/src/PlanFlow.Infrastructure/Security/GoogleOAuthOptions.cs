namespace PlanFlow.Infrastructure.Security;

/// <summary>Bound from the "GoogleOAuth" configuration section. ClientId/ClientSecret come from the Google Cloud Console OAuth client (free tier, no billing required for Calendar API read scopes).</summary>
public class GoogleOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Must exactly match a redirect URI registered on the Google OAuth client (e.g. https://api.planflow.dev/api/calendar/google/callback).</summary>
    public string RedirectUri { get; set; } = string.Empty;

    public string Scopes { get; set; } = "openid email https://www.googleapis.com/auth/calendar.readonly";

    public string AuthorizationEndpoint { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";
    public string UserInfoEndpoint { get; set; } = "https://openidconnect.googleapis.com/v1/userinfo";
}
