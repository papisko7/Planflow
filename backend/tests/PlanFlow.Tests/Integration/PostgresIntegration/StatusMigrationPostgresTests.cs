using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// Proves the migration on a real PostgreSQL database that already holds data: rows written under the OLD schema
/// (Todo/Blocked/Done statuses, no new columns) must come out with the new statuses and sensible defaults.
/// Uses its own container because it has to stop at the previous migration, fill it with data and only then migrate on.
/// </summary>
public class StatusMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261017140904_CalendarItemFoundation";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static async Task InsertItemAsync(PlanFlowDbContext db, string title, int taskType, int status) =>
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO calendar_items (\"Id\", \"CreatedAtUtc\", \"TeamId\", \"Title\", \"Status\", \"TaskType\", \"CurrentUrgencyScore\") " +
            $"VALUES ('{Guid.NewGuid()}', now(), '{Guid.NewGuid()}', '{title}', {status}, {taskType}, 0)");

    private static async Task<int> ScalarAsync(PlanFlowDbContext db, string sql) =>
        (await db.Database.SqlQueryRaw<int>(sql).ToListAsync()).Single();

    private static Task<int> StatusOfAsync(PlanFlowDbContext db, string title) =>
        ScalarAsync(db, $"SELECT \"Status\" AS \"Value\" FROM calendar_items WHERE \"Title\" = '{title}'");

    [Fact]
    public async Task Migrate_ExistingRows_GetNewStatusesAndDefaults()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync(); // one connection for everything, so the session setting below applies to all statements

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        // The rows point at teams that do not exist; switch FK checks off for this session (the container user is a superuser).
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");

        // Old numbers: 0 Todo, 1 InProgress, 2 Blocked, 3 Done, 4 Cancelled. TaskType: 0 Task, 1 Event.
        await InsertItemAsync(db, "task-todo", 0, 0);
        await InsertItemAsync(db, "task-blocked", 0, 2);
        await InsertItemAsync(db, "task-done", 0, 3);
        await InsertItemAsync(db, "task-cancelled", 0, 4);
        await InsertItemAsync(db, "event-todo", 1, 0);
        await InsertItemAsync(db, "event-inprogress", 1, 1);
        await InsertItemAsync(db, "event-done", 1, 3);
        await InsertItemAsync(db, "event-cancelled", 1, 4);

        await migrator.MigrateAsync();

        Assert.Equal(0, await StatusOfAsync(db, "task-todo"));       // Backlog
        Assert.Equal(0, await StatusOfAsync(db, "task-blocked"));    // Blocked is retired -> Backlog
        Assert.Equal(3, await StatusOfAsync(db, "task-done"));       // Completed (same number)
        Assert.Equal(4, await StatusOfAsync(db, "task-cancelled"));  // unchanged
        Assert.Equal(5, await StatusOfAsync(db, "event-todo"));      // Confirmed
        Assert.Equal(5, await StatusOfAsync(db, "event-inprogress"));
        Assert.Equal(5, await StatusOfAsync(db, "event-done"));
        Assert.Equal(4, await StatusOfAsync(db, "event-cancelled")); // Cancelled stays Cancelled

        // Old rows are Busy (events always blocked time), not all-day, with the default template and no colour.
        Assert.Equal(8, await ScalarAsync(db, "SELECT count(*)::int AS \"Value\" FROM calendar_items WHERE \"IsBusy\" AND NOT \"IsAllDay\" AND \"EventTemplate\" = 0 AND \"ColorKey\" IS NULL"));
    }

    [Fact]
    public async Task MigrateDown_RestoresTheOldMeaningOfEventStatuses()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync();

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        await InsertItemAsync(db, "event-confirmed", 1, 5);
        await InsertItemAsync(db, "event-tentative", 1, 6);

        await migrator.MigrateAsync(PreviousMigration);

        Assert.Equal(0, await StatusOfAsync(db, "event-confirmed")); // back to Todo
        Assert.Equal(0, await StatusOfAsync(db, "event-tentative"));
    }
}
