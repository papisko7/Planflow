using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The 7.5.8 migration adds a "daily reminder" switch to settings rows that already exist. EF would fill them with false;
/// the migration is edited so that existing users get the same default as new ones (on). Checked on a real PostgreSQL database.
/// </summary>
public class ReminderPreferenceMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261018140313_ItemTemplatesStatusesAndTimeZone";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_reminder_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrate_ExistingSettingsRows_GetTheDailyReminderSwitchedOn()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync();

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        // A settings row written before the column existed (the user does not exist; skip FK checks for this session).
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO notification_preferences (\"Id\", \"CreatedAtUtc\", \"UserId\", \"EmailEnabled\", \"PushEnabled\", \"LoginAlerts\", " +
            "\"TaskAssignments\", \"TaskChanges\", \"TeamTasks\", \"TaskUrgency\", \"TeamMembership\", \"ChatDigest\", \"CalendarIssues\") " +
            $"VALUES ('{Guid.NewGuid()}', now(), '{Guid.NewGuid()}', true, true, 0, true, true, false, true, true, false, true)");

        await migrator.MigrateAsync();

        var switchedOn = (await db.Database
            .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM notification_preferences WHERE \"IncompleteItemsReminder\" AND \"LastIncompleteReminderSentAtUtc\" IS NULL")
            .ToListAsync()).Single();
        Assert.Equal(1, switchedOn);
    }
}
