using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// Boots the real <c>PlanFlow.Api</c> host (real JWT/RBAC/rate-limiting pipeline, same as
/// <see cref="PlanFlow.Tests.Api.CustomWebApplicationFactory"/>) under the "PostgresIntegrationTesting"
/// environment (see <see cref="PlanFlow.Infrastructure.DependencyInjection.AddInfrastructure"/>), which
/// backs <see cref="PlanFlowDbContext"/> with a real, disposable PostgreSQL instance via Testcontainers
/// instead of the "Testing" environment's InMemory provider. This is what actually exercises Npgsql SQL
/// translation, real migrations, and real foreign-key cascade behavior — none of which the InMemory
/// provider validates.
/// One container is shared across all tests in the "Postgres" collection (see <see cref="PostgresCollection"/>)
/// to keep the suite fast; tests stay isolated from each other by always generating fresh Guids for
/// teams/users/tasks rather than relying on a clean database per test.
/// </summary>
public class PostgresWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("PostgresIntegrationTesting");

        // Feeds the container's connection string in as plain configuration instead of swapping DI
        // registrations after the fact — AddInfrastructure's "PostgresIntegrationTesting" branch calls
        // AddDbContext exactly once, so there's no risk of two EF providers (InMemory + Npgsql) ending
        // up registered on the same IServiceCollection, which EF Core rejects outright.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PlanFlowDb"] = _postgres.GetConnectionString()
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Force the host to build now (Services triggers WebApplicationFactory's lazy host creation)
        // so ConfigureWebHost has run and PlanFlowDbContext is wired to the container, then apply the
        // real EF Core migrations so tests run against production schema, not an EnsureCreated snapshot.
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        await context.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
