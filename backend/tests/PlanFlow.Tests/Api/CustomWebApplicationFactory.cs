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
        builder.UseEnvironment("Testing");
    }
}
