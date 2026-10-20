using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The migration adds the nullable ExternalFieldsHash column to calendar_items. Existing items must survive untouched (their hash is
/// simply empty, so the next sync takes Google's values once) and the migration must be reversible. Checked on a real PostgreSQL database.
/// </summary>
public class ExternalFieldsHashMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261019140532_RemoveUnusedAiColumns";
    private const string ThisMigration = "20261020083827_ExternalFieldsHash";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_external_fields_hash_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrate_AddsTheColumn_KeepsTheItems_AndCanBeRolledBack()
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
        Assert.Equal(0, await CountColumnAsync(db));

        await migrator.MigrateAsync(ThisMigration);

        Assert.Equal(1, await CountColumnAsync(db));
        // Read with SQL: the entity already has columns that later migrations add.
        var title = await db.Database.SqlQuery<string>($"SELECT \"Title\" AS \"Value\" FROM calendar_items WHERE \"Id\" = {itemId}").SingleAsync();
        var hashes = await db.Database.SqlQuery<int>($"SELECT count(\"ExternalFieldsHash\")::int AS \"Value\" FROM calendar_items WHERE \"Id\" = {itemId}").SingleAsync();
        Assert.Equal("Old item", title);
        Assert.Equal(0, hashes);   // no hash yet: the next sync takes Google's values once and stores it

        await migrator.MigrateAsync(PreviousMigration);

        Assert.Equal(0, await CountColumnAsync(db));
    }

    private static Task<int> CountColumnAsync(PlanFlowDbContext db) =>
        db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'calendar_items' AND column_name = 'ExternalFieldsHash'")
            .SingleAsync();
}
