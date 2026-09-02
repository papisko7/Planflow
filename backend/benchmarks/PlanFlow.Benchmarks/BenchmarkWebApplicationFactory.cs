using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlanFlow.Api;
using PlanFlow.Api.AuthPolicy;
using PlanFlow.Api.Middleware;
using PlanFlow.Application;
using PlanFlow.Infrastructure;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace PlanFlow.Benchmarks;

/// <summary>
/// Self-hosts the real Api composition (same extension methods/middleware order as
/// <c>PlanFlow.Api/Program.cs</c>) on <see cref="TestServer"/> against a real, Testcontainers-provisioned
/// PostgreSQL instance, under the "PostgresIntegrationTesting" environment (see
/// <see cref="PlanFlow.Infrastructure.DependencyInjection.AddInfrastructure"/> - real Npgsql, in-memory
/// distributed cache, no real Redis).
/// </summary>
/// <remarks>
/// Deliberately does NOT use <c>WebApplicationFactory&lt;Program&gt;</c> (as
/// <c>PlanFlow.Tests/Api/CustomWebApplicationFactory</c> does): that type resolves the SUT's content
/// root either from an assembly-level <c>WebApplicationFactoryContentRootAttribute</c> or a generated
/// manifest file, both wired up by MSBuild targets that only fire reliably for
/// <c>Microsoft.NET.Test.Sdk</c> test projects. This project is a plain console app (it needs to be
/// directly runnable via <c>dotnet run</c> for repeatable local benchmarking), so that resolution
/// silently guessed a nonexistent directory and crashed before <c>ConfigureWebHost</c> ever got a
/// chance to fix it. Composing the host ourselves with the exact same extension method calls as
/// <c>Program.cs</c> sidesteps that resolution entirely while still exercising the real DI wiring,
/// middleware pipeline, and controllers.
///
/// Every scenario boots its own instance of this factory, and <c>BenchmarkHost.Main</c> starts ALL
/// of them up front and stops them only after every scenario has finished - never
/// start-run-stop-then-start-the-next-one. Quartz's logging bridge
/// (<c>Quartz.Logging.LogProvider</c>) is a process-wide static that caches whichever host's
/// <c>ILoggerFactory</c> it saw most recently; disposing one host's factory and then booting a
/// second Quartz-using host in the same process throws <see cref="ObjectDisposedException"/>
/// ("LoggerFactory") the moment the new host's scheduler starts up. Keeping every host alive until
/// all scenarios are done avoids that entirely (mirrors the fix documented on
/// <c>PlanFlow.Tests/Api/SecurityIntegrationTests</c>, which hit the same static via concurrent
/// hosts rather than sequential ones).
/// </remarks>
public sealed class BenchmarkWebApplicationFactory : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_bench")
        .WithUsername("planflow")
        .WithPassword("planflow_bench_pw")
        .Build();

    private IHost? _host;

    public IServiceProvider Services => _host!.Services;

    public HttpClient CreateClient() => _host!.GetTestServer().CreateClient();

    public async Task StartAsync()
    {
        await _postgres.StartAsync();

        var apiContentRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PlanFlow.Api"));

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment("PostgresIntegrationTesting");
                webBuilder.UseContentRoot(apiContentRoot);

                webBuilder.ConfigureAppConfiguration(config =>
                {
                    config.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false);
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PlanFlowDb"] = _postgres.GetConnectionString()
                    });
                });

                webBuilder.ConfigureServices((context, services) =>
                {
                    services.AddApplicationServices();
                    services.AddInfrastructure(context.Configuration, context.HostingEnvironment);

                    // MVC's default ApplicationPartManager scans Assembly.GetEntryAssembly() for
                    // controllers - that's THIS benchmark console app, not PlanFlow.Api.dll where
                    // TasksController etc. actually live, so every route 404s without this explicit part.
                    services.AddControllers()
                        .AddApplicationPart(typeof(PlanFlow.Api.Controllers.TasksController).Assembly);

                    services.AddJwtAuthentication(context.Configuration);
                    services.AddRbacPolicies();
                    services.AddApiRateLimiting();
                });

                webBuilder.Configure(app =>
                {
                    // Mirrors PlanFlow.Api/Program.cs's pipeline (minus dev-only Swagger and
                    // HTTPS redirection, both irrelevant under TestServer's in-memory transport).
                    app.UseMiddleware<ExceptionHandlingMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseRateLimiter();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            });

        _host = await hostBuilder.StartAsync();

        using var scope = _host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        await context.Database.MigrateAsync();
    }

    public async Task StopAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _postgres.DisposeAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
