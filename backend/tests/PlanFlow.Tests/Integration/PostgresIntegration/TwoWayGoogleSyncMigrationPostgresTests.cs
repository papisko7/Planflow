using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The migration adds the two columns that note an edit of an imported item waiting to be sent to Google (the fields and the time of the edit).
/// Existing items must survive untouched with "nothing waiting", and the migration must be reversible. Checked on a real PostgreSQL database.
/// </summary>
public class TwoWayGoogleSyncMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261020091347_IncompleteReminderCounter";
    private const string ThisMigration = "20261020100938_TwoWayGoogleSync";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_two_way_sync_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrate_AddsTheColumns_KeepsTheItemsWithNothingWaiting_AndCanBeRolledBack()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var teamId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync($"INSERT INTO teams (\"Id\", \"CreatedAtUtc\", \"Name\", \"IsPersonal\") VALUES ('{teamId}', now(), 'T', false)");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO calendar_items (\"Id\", \"CreatedAtUtc\", \"TeamId\", \"Title\", \"Status\", \"TaskType\", \"EventTemplate\", \"IsBusy\", \"IsAllDay\", " +
            "\"IsDeadlineDefaulted\", \"IsAssignmentLocked\", \"IsDataIncomplete\", \"CurrentUrgencyScore\") " +
            $"VALUES ('{itemId}', now(), '{teamId}', 'Old item', 0, 0, 0, true, false, false, false, false, 0.42)");
        Assert.Equal(0, await CountColumnsAsync(db));

        await migrator.MigrateAsync(ThisMigration);

        Assert.Equal(2, await CountColumnsAsync(db));
        // Read with SQL: the entity may gain columns in later migrations.
        var title = await db.Database.SqlQuery<string>($"SELECT \"Title\" AS \"Value\" FROM calendar_items WHERE \"Id\" = {itemId}").SingleAsync();
        var fields = await db.Database.SqlQuery<int>($"SELECT \"LocalEditedFields\" AS \"Value\" FROM calendar_items WHERE \"Id\" = {itemId}").SingleAsync();
        var times = await db.Database.SqlQuery<int>($"SELECT count(\"LocalEditedAtUtc\")::int AS \"Value\" FROM calendar_items WHERE \"Id\" = {itemId}").SingleAsync();
        Assert.Equal("Old item", title);
        Assert.Equal(0, fields);   // nothing waits to be sent
        Assert.Equal(0, times);

        await migrator.MigrateAsync(PreviousMigration);

        Assert.Equal(0, await CountColumnsAsync(db));
    }

    private static Task<int> CountColumnsAsync(PlanFlowDbContext db) =>
        db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'calendar_items' AND column_name IN ('LocalEditedFields', 'LocalEditedAtUtc')")
            .SingleAsync();
}
