using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.AiPlanner.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.AiClients;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Caching;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Infrastructure.Security;
using Quartz;

namespace PlanFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
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
        services.AddSingleton<ICacheService, RedisCacheService>();

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // Register AI Planner client based on configuration (mock by default)
        services.AddHttpClient();
        services.AddSingleton<IAiPlannerClient>(sp =>
            AiPlannerClientFactory.CreateClient(configuration, sp.GetRequiredService<IHttpClientFactory>().CreateClient()));

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
