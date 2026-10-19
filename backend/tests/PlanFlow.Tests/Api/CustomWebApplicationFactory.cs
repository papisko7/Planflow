using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Boots the real <c>PlanFlow.Api</c> host (real middleware pipeline, real JWT/RBAC/rate-limiting
/// config) under the "Testing" environment, which <see cref="PlanFlow.Infrastructure.DependencyInjection.AddInfrastructure"/>
/// checks to swap PostgreSQL/Redis for a throwaway InMemory database and cache — see that method
/// for why a plain DI-descriptor swap here doesn't work with <c>AddDbContextPool</c>. Everything
/// else (Quartz jobs, data protection, HTTP clients) runs unchanged.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Quartz caches the previous host's (now disposed) LoggerFactory in a static; clear it so
        // back-to-back hosts in one test run do not hit ObjectDisposedException during startup.
        Quartz.Logging.LogProvider.SetCurrentLogProvider(null);
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Also after a host: the provider Quartz cached still points at this host's disposed LoggerFactory, and the scheduler of a host
        // that outlives this one (the PostgreSQL integration host) would fail to start its next job ("Sync now") with an ObjectDisposedException.
        Quartz.Logging.LogProvider.SetCurrentLogProvider(null);
    }
}
