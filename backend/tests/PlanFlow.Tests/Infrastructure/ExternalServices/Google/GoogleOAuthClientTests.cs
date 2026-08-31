using Microsoft.Extensions.Options;
using PlanFlow.Infrastructure.ExternalServices.Google;
using PlanFlow.Infrastructure.Security;
using Xunit;

namespace PlanFlow.Tests.Infrastructure.ExternalServices.Google;

public class GoogleOAuthClientTests
{
    private static GoogleOAuthClient CreateClient(GoogleOAuthOptions? options = null) =>
        new(new HttpClient(), Options.Create(options ?? new GoogleOAuthOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            RedirectUri = "https://localhost:5443/api/calendar/google/callback",
            Scopes = "openid email https://www.googleapis.com/auth/calendar.readonly"
        }));

    [Fact]
    public void BuildAuthorizationUrl_IncludesPkceStateAndOfflineConsentParameters()
    {
        var client = CreateClient();

        var url = client.BuildAuthorizationUrl(state: "state-123", codeChallenge: "challenge-456");

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        Assert.Contains("client_id=test-client-id", url);
        Assert.Contains("state=state-123", url);
        Assert.Contains("code_challenge=challenge-456", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("access_type=offline", url);
        Assert.Contains("prompt=consent", url);
        Assert.Contains("response_type=code", url);
    }

    [Fact]
    public void BuildAuthorizationUrl_RedirectUriRoundTripsThroughQueryStringEncoding()
    {
        var client = CreateClient();

        var url = client.BuildAuthorizationUrl("state-123", "challenge-456");
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);

        Assert.Equal("https://localhost:5443/api/calendar/google/callback", query["redirect_uri"]);
    }
}
