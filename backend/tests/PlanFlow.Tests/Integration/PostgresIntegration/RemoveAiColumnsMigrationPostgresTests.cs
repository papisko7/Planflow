using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The migration drops the three unused columns of calendar_items. Existing items must survive untouched and the
/// migration must be reversible. Checked on a real PostgreSQL database.
/// </summary>
public class RemoveAiColumnsMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261019062053_RepeatRulesAndSeries";
    private const string ThisMigration = "20261019140532_RemoveUnusedAiColumns";

    private static readonly string[] DroppedColumns = ["UseAiEnhancement", "AiImpactScore", "AiImpactReasoning"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_remove_ai_columns_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrate_DropsTheColumns_KeepsTheItems_AndCanBeRolledBack()
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
            "\"IsDeadlineDefaulted\", \"IsAssignmentLocked\", \"UseAiEnhancement\", \"IsDataIncomplete\", \"CurrentUrgencyScore\") " +
            $"VALUES ('{itemId}', now(), '{teamId}', 'Old item', 0, 0, 0, true, false, false, false, false, false, 0.42)");
        Assert.Equal(DroppedColumns.Length, await CountDroppedColumnsAsync(db));

        await migrator.MigrateAsync(ThisMigration);

        Assert.Equal(0, await CountDroppedColumnsAsync(db));
        // Read with SQL: the entity already has columns that later migrations add.
        var title = await db.Database.SqlQuery<string>($"SELECT \"Title\" AS \"Value\" FROM calendar_items WHERE \"Id\" = {itemId}").SingleAsync();
        var score = await db.Database.SqlQuery<double>($"SELECT \"CurrentUrgencyScore\" AS \"Value\" FROM calendar_items WHERE \"Id\" = {itemId}").SingleAsync();
        Assert.Equal("Old item", title);
        Assert.Equal(0.42, score);

        await migrator.MigrateAsync(PreviousMigration);

        Assert.Equal(DroppedColumns.Length, await CountDroppedColumnsAsync(db));
    }

    private static Task<int> CountDroppedColumnsAsync(PlanFlowDbContext db) =>
        db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'calendar_items' AND column_name = ANY ({DroppedColumns})")
            .SingleAsync();
}
