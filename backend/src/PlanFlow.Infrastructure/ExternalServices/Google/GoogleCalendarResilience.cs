using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace PlanFlow.Infrastructure.ExternalServices.Google;

/// <summary>
/// Retry policy for calls to Google's Calendar API. Factored out of DependencyInjection so
/// integration tests can build the exact same pipeline around a stubbed HttpMessageHandler
/// (see PlanFlow.Tests/Integration/CalendarSyncIntegrationTests) instead of duplicating it.
/// </summary>
public static class GoogleCalendarResilience
{
    public static IHttpClientBuilder AddGoogleCalendarRetry(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler("google-calendar-retry", pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(100),
                UseJitter = true,
                // Retry on 429 (rate limit) and 5xx (transient Google-side failure); the base
                // HttpClientTransientHttpStatusCodePredicate already covers 5xx/408/network
                // exceptions, so this only needs to add 429 on top of it.
                ShouldHandle = args => args switch
                {
                    { Outcome.Result.StatusCode: HttpStatusCode.TooManyRequests } => PredicateResult.True(),
                    _ => HttpClientResiliencePredicates.IsTransient(args.Outcome) ? PredicateResult.True() : PredicateResult.False()
                }
            });
        });

        return builder;
    }
}
