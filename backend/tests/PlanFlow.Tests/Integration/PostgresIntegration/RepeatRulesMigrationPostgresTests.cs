using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The migration replaces the three repeat columns of an item (an interval in hours, an end, a flag) with a series that the
/// occurrences point at. Every item that was still repeating must come out as the first occurrence of an equivalent series, and the
/// migration must be reversible. Checked on a real PostgreSQL database.
/// </summary>
public class RepeatRulesMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261018235938_MemberCalendarsAndPrivacy";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_repeat_rules_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrate_TurnsEveryRepeatingItemIntoTheFirstOccurrenceOfASeries_AndCanBeRolledBack()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var weeklyTask = Guid.NewGuid();
        var threeDayEvent = Guid.NewGuid();
        var plain = Guid.NewGuid();
        var deadline = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        var start = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlRawAsync($"INSERT INTO teams (\"Id\", \"CreatedAtUtc\", \"Name\", \"IsPersonal\") VALUES ('{teamId}', now(), 'T', false)");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO users (\"Id\", \"CreatedAtUtc\", \"DisplayName\", \"Email\", \"PasswordHash\", \"CalendarSharingLevel\", \"TimeZoneId\") " +
            $"VALUES ('{userId}', now(), 'Anna', '{userId:N}@example.com', 'x', 1, 'Europe/Warsaw')");

        const string columns = "(\"Id\", \"CreatedAtUtc\", \"TeamId\", \"AssignedUserId\", \"Title\", \"Status\", \"TaskType\", \"EventTemplate\", \"IsBusy\", \"IsAllDay\", " +
                               "\"IsDeadlineDefaulted\", \"IsAssignmentLocked\", \"IsRecurring\", \"RecurrenceIntervalHours\", \"RecurrenceEndsAtUtc\", " +
                               "\"DeadlineUtc\", \"StartUtc\", \"UseAiEnhancement\", \"IsDataIncomplete\", \"CurrentUrgencyScore\")";
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO calendar_items {columns} VALUES ('{weeklyTask}', now(), '{teamId}', '{userId}', 'Report', 0, 0, 0, true, false, false, false, " +
            $"true, 336, '2027-01-01T00:00:00Z', '{deadline:O}', NULL, false, false, 0)");
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO calendar_items {columns} VALUES ('{threeDayEvent}', now(), '{teamId}', '{userId}', 'Sync', 5, 1, 0, true, false, false, false, " +
            $"true, 72, NULL, NULL, '{start:O}', false, false, 0)");
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO calendar_items {columns} VALUES ('{plain}', now(), '{teamId}', NULL, 'Once', 0, 0, 0, true, false, false, false, " +
            "false, NULL, NULL, NULL, NULL, false, false, 0)");

        await migrator.MigrateAsync();

        var series = await db.RecurrenceSeries.AsNoTracking().ToListAsync();
        Assert.Equal(2, series.Count);

        var weekly = series.Single(s => s.Id == weeklyTask);
        Assert.Equal(RecurrenceFrequency.Weekly, weekly.Frequency);
        Assert.Equal(2, weekly.Interval);
        Assert.Equal(TaskType.Task, weekly.TaskType);
        Assert.Equal(deadline, weekly.AnchorUtc);
        Assert.Equal(deadline, weekly.GeneratedThroughUtc);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), weekly.EndsAtUtc);
        Assert.Equal("Europe/Warsaw", weekly.TimeZoneId);

        var daily = series.Single(s => s.Id == threeDayEvent);
        Assert.Equal(RecurrenceFrequency.Daily, daily.Frequency);
        Assert.Equal(3, daily.Interval);
        Assert.Equal(TaskType.Event, daily.TaskType);
        Assert.Equal(start, daily.AnchorUtc);

        var items = await db.CalendarItems.AsNoTracking().ToListAsync();
        Assert.Equal(weeklyTask, items.Single(i => i.Id == weeklyTask).RecurrenceSeriesId);
        Assert.Equal(threeDayEvent, items.Single(i => i.Id == threeDayEvent).RecurrenceSeriesId);
        Assert.Null(items.Single(i => i.Id == plain).RecurrenceSeriesId);
        Assert.True(items.Single(i => i.Id == weeklyTask).IsRecurring);
        Assert.False(items.Single(i => i.Id == plain).IsRecurring);

        // And back: the latest occurrence of each series is the "repeating" one again, with its interval in hours.
        await migrator.MigrateAsync(PreviousMigration);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT \"Id\", \"IsRecurring\", \"RecurrenceIntervalHours\" FROM calendar_items ORDER BY \"Title\"";
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<Guid, (bool Recurring, int? Hours)>();
        while (await reader.ReadAsync())
        {
            rows[reader.GetGuid(0)] = (reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetInt32(2));
        }

        Assert.Equal((true, 336), rows[weeklyTask]);
        Assert.Equal((true, 72), rows[threeDayEvent]);
        Assert.Equal((false, null), rows[plain]);
    }
}
