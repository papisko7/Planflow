using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Caching;
using PlanFlow.Infrastructure.Email;
using PlanFlow.Infrastructure.ExternalServices.Google;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Infrastructure.Presence;
using PlanFlow.Infrastructure.Push;
using PlanFlow.Infrastructure.Security;
using Quartz;
using StackExchange.Redis;

namespace PlanFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // The test environments swap the database and cache for in-memory ones; everything else runs unchanged.
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
            services.AddSingleton<IChatPresence, InMemoryChatPresence>();
        }
        else if (environment.IsEnvironment("PostgresIntegrationTesting"))
        {
            // The same host against a real PostgreSQL (Testcontainers). Its own branch: EF Core rejects two database providers
            // registered on one service collection.
            services.AddDbContext<PlanFlowDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("PlanFlowDb")));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PlanFlowDbContext>());
            services.AddDistributedMemoryCache();
            services.AddSingleton<IChatPresence, InMemoryChatPresence>();
        }
        else
        {
            // Pooled context: reuses DbContext instances and, with them, Npgsql's connection pool.
            services.AddDbContextPool<PlanFlowDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("PlanFlowDb")));

            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PlanFlowDbContext>());

            var redisConnectionString = configuration.GetConnectionString("Redis");
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "planflow:";
            });

            AddChatPresence(services, redisConnectionString);
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

        // E-mail: SMTP in normal runs (Mailpit locally, a real provider via user-secrets); under the test environments
        // mail is kept in memory so tests can read it and nothing leaves the machine.
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        if (environment.IsEnvironment("Testing") || environment.IsEnvironment("PostgresIntegrationTesting"))
        {
            services.AddSingleton<InMemoryEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<InMemoryEmailSender>());
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        // Push: Expo push service in normal runs; kept in memory under the test environments so nothing leaves the machine.
        services.Configure<PushOptions>(configuration.GetSection("Push"));
        if (environment.IsEnvironment("Testing") || environment.IsEnvironment("PostgresIntegrationTesting"))
        {
            services.AddSingleton<InMemoryPushSender>();
            services.AddSingleton<IPushSender>(sp => sp.GetRequiredService<InMemoryPushSender>());
        }
        else
        {
            services.AddHttpClient<IPushSender, ExpoPushSender>(client => client.Timeout = TimeSpan.FromSeconds(15));
        }

        services.Configure<GoogleOAuthOptions>(configuration.GetSection("GoogleOAuth"));
        services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>();
        services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>().AddGoogleCalendarRetry();
        services.AddHttpClient<IGoogleTasksClient, GoogleTasksClient>().AddGoogleCalendarRetry();

        services.AddQuartz(quartz =>
        {
            // Google is fetched only when a user presses "Sync now": these two jobs are stored without a trigger and started by
            // GoogleSyncTrigger. That keeps the number of requests to Google under the user's control and "Last sync" truthful.
            quartz.AddJob<SyncCalendarJob>(opts => opts.WithIdentity(GoogleSyncTrigger.CalendarJob).StoreDurably());
            quartz.AddJob<SyncTasksJob>(opts => opts.WithIdentity(GoogleSyncTrigger.TasksJob).StoreDurably());
            AddIntervalJob<PrioritizationJob>(quartz, "PrioritizationJob", TimeSpan.FromHours(1));
            AddIntervalJob<AlertingJob>(quartz, "AlertingJob", TimeSpan.FromMinutes(1));
            AddIntervalJob<ItemReminderJob>(quartz, "ItemReminderJob", TimeSpan.FromMinutes(1));
            AddIntervalJob<EmailOutboxJob>(quartz, "EmailOutboxJob", TimeSpan.FromSeconds(30));
            AddIntervalJob<PushDeliveryJob>(quartz, "PushDeliveryJob", TimeSpan.FromSeconds(10));
            AddIntervalJob<ChatDigestJob>(quartz, "ChatDigestJob", TimeSpan.FromMinutes(15));
            AddIntervalJob<RecurringItemJob>(quartz, "RecurringItemJob", TimeSpan.FromMinutes(1));
            AddIntervalJob<IncompleteItemsReminderJob>(quartz, "IncompleteItemsReminderJob", TimeSpan.FromMinutes(15));

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

    /// <summary>
    /// "Who is online" must be visible to every server instance, so it lives in Redis. Without a Redis connection string the
    /// in-memory version is used, which is correct for a single instance.
    /// </summary>
    private static void AddChatPresence(IServiceCollection services, string? redisConnectionString)
    {
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IChatPresence, InMemoryChatPresence>();
            return;
        }

        // AbortOnConnectFail=false: the app starts even when Redis is down and keeps retrying in the background.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnectionString);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2000;
            options.AsyncTimeout = 2000;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<RedisChatPresence>();
        services.AddSingleton<IChatPresence>(sp => sp.GetRequiredService<RedisChatPresence>());
        services.AddHostedService<ChatPresenceHeartbeat>();
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
