using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace PlanFlow.Api;

/// <summary>
/// Fixed-window request throttling using ASP.NET Core's built-in limiter (no extra NuGet package
/// needed on .NET 7+). Two tiers: a generous global limit covering every endpoint, and a tight
/// "auth" tier applied to credential/brute-force-prone endpoints (login, register, OAuth exchange).
/// </summary>
public static class RateLimitingConfig
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partition by authenticated user id when available, falling back to client IP for
            // anonymous callers — so one abusive user can't exhaust the limit for everyone else.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
                    ? $"user:{httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value}"
                    : $"ip:{httpContext.Connection.RemoteIpAddress}";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });

            // Stricter, IP-partitioned tier for auth endpoints: limits brute-force login/register
            // attempts regardless of whether the caller is authenticated yet.
            options.AddPolicy(AuthPolicy, httpContext =>
            {
                var partitionKey = $"ip:{httpContext.Connection.RemoteIpAddress}";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
        });

        return services;
    }
}
