using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Caching;
using PlanFlow.Infrastructure.ExternalServices.Google;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Infrastructure.Security;
using Quartz;

namespace PlanFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // WebApplicationFactory-based integration tests (PlanFlow.Tests/Api) run the real Api host
        // under the "Testing" environment so they get a throwaway InMemory database and cache
        // instead of needing a real PostgreSQL/Redis instance — everything else in this method
        // (Quartz, data protection, HTTP clients) still runs unchanged for those tests.
        if (environment.IsEnvironment("Testing"))
        {
            // The database name must be captured once outside the options delegate — AddDbContext
            // re-invokes this delegate to build fresh DbContextOptions per scope, so a Guid.NewGuid()
            // call inside it would hand every scope its own empty database instead of a shared one.
            var testingDatabaseName = $"planflow-testing-{Guid.NewGuid()}";
            services.AddDbContext<PlanFlowDbContext>(options =>
                options.UseInMemoryDatabase(testingDatabaseName));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PlanFlowDbContext>());
            services.AddDistributedMemoryCache();
        }
        else if (environment.IsEnvironment("PostgresIntegrationTesting"))
        {
            // PlanFlow.Tests/Integration/Postgres runs the real Api host against a real,
            // Testcontainers-provisioned PostgreSQL instance (ConnectionStrings:PlanFlowDb points at
            // the container) instead of InMemory, so real SQL translation/migrations/FK cascades are
            // actually exercised. Kept as its own branch (not layered onto "Testing" above) because
            // registering both UseInMemoryDatabase and UseNpgsql on the same IServiceCollection makes
            // EF Core throw ("multiple database providers registered") — only one AddDbContext call
            // may ever run per service collection. Cache stays in-memory, same as "Testing", since
            // these tests don't need a real Redis instance either.
            services.AddDbContext<PlanFlowDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("PlanFlowDb")));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PlanFlowDbContext>());
            services.AddDistributedMemoryCache();
        }
        else
        {
            // Pooled context (vs. AddDbContext's per-request "new instance") reuses a fixed number of
            // DbContext instances across requests, which in turn reuses Npgsql's own connection pool
            // instead of opening/closing a physical connection per request.
            services.AddDbContextPool<PlanFlowDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("PlanFlowDb")));

            // Resolve the Application-layer abstraction to the same pooled DbContext instance.
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PlanFlowDbContext>());

            var redisConnectionString = configuration.GetConnectionString("Redis");
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "planflow:";
            });
        }

        services.AddSingleton<ICacheService, RedisCacheService>();

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // Keys persisted to disk (not the default in-memory ring) so encrypted CalendarIntegration
        // tokens stay decryptable across app restarts/redeploys instead of being silently orphaned.
        services.AddDataProtection()
            .SetApplicationName("PlanFlow")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "dataprotection-keys")));
        services.AddSingleton<ITokenEncryptionService, DataProtectionTokenEncryptionService>();

        services.Configure<GoogleOAuthOptions>(configuration.GetSection("GoogleOAuth"));
        services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>();
        services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>().AddGoogleCalendarRetry();

        services.AddQuartz(quartz =>
        {
            // Each job gets its own JobKey/trigger pair; Quartz's DI integration opens a fresh
            // DI scope per execution, so jobs can safely take scoped services like IApplicationDbContext.
            AddIntervalJob<SyncCalendarJob>(quartz, "SyncCalendarJob", TimeSpan.FromMinutes(5));
            AddIntervalJob<PrioritizationJob>(quartz, "PrioritizationJob", TimeSpan.FromHours(1));
            AddIntervalJob<AlertingJob>(quartz, "AlertingJob", TimeSpan.FromMinutes(1));

            var cleanupJobKey = new JobKey("CleanupJob");
            quartz.AddJob<CleanupJob>(opts => opts.WithIdentity(cleanupJobKey));
            quartz.AddTrigger(opts => opts
                .ForJob(cleanupJobKey)
                .WithIdentity("CleanupJob-trigger")
                // Daily at 02:00 UTC, when task/alert traffic is at its lowest.
                .WithCronSchedule("0 0 2 * * ?"));
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return services;
    }

    private static void AddIntervalJob<TJob>(IServiceCollectionQuartzConfigurator quartz, string name, TimeSpan interval)
        where TJob : IJob
    {
        var jobKey = new JobKey(name);
        quartz.AddJob<TJob>(opts => opts.WithIdentity(jobKey));
        quartz.AddTrigger(opts => opts
            .ForJob(jobKey)
            .WithIdentity($"{name}-trigger")
            .WithSimpleSchedule(schedule => schedule.WithInterval(interval).RepeatForever())
            .StartNow());
    }
}
