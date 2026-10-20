using System.Net;
using PlanFlow.Application.Common.Exceptions;

namespace PlanFlow.Infrastructure.ExternalServices.Google;

/// <summary>
/// Sorts Google's answer to a change (events.patch, tasks.patch) into the three outcomes the sync jobs react to differently:
/// the token is no longer accepted (<see cref="UnauthorizedException"/>), Google refuses this change for good
/// (<see cref="GoogleWriteRejectedException"/>) or the failure is temporary (<see cref="HttpRequestException"/>, left to the next sync).
/// </summary>
internal static class GoogleWriteErrors
{
    /// <summary>Returns normally for a successful answer; throws the matching exception otherwise.</summary>
    public static async Task EnsureAcceptedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = response.StatusCode;
        if (status == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedException("Google rejected the request (expired/revoked access token).");
        }

        if (status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new GoogleWriteRejectedException(GoogleWriteFailure.Gone, "The event or task no longer exists in Google.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (status == HttpStatusCode.Forbidden)
        {
            if (Mentions(body, "insufficientPermissions", "ACCESS_TOKEN_SCOPE_INSUFFICIENT", "insufficient authentication scopes"))
            {
                throw new GoogleWriteRejectedException(GoogleWriteFailure.MissingPermission, "The account did not allow PlanFlow to change its Google data.");
            }

            // Google answers a burst of requests with 403 as well; that is a "try later", not a refusal.
            if (Mentions(body, "rateLimitExceeded", "userRateLimitExceeded", "quotaExceeded", "RATE_LIMIT_EXCEEDED"))
            {
                throw new HttpRequestException("Google asked to slow down.", inner: null, status);
            }
        }

        // 408 and 429 are retried by the resilience pipeline and then reported as failures to try again; the rest of the 4xx is final.
        if ((int)status is >= 400 and < 500 && status is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))
        {
            throw new GoogleWriteRejectedException(GoogleWriteFailure.Rejected, $"Google refused the change ({(int)status}).");
        }

        response.EnsureSuccessStatusCode();
    }

    private static bool Mentions(string body, params string[] markers) =>
        markers.Any(marker => body.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
