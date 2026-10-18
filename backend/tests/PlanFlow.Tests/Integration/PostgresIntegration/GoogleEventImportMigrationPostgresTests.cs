using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The 7.5.2 migration adds the reminders table and the "last full pass" moment of a Google connection. Checked on a real
/// PostgreSQL database: connections that already exist must be due for a full pass (so they pick up the new fields), and the table's
/// rule "exactly one reminder form" and its cascade must really hold.
/// </summary>
public class GoogleEventImportMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261018151912_IncompleteItemsReminderPreference";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_event_import_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private PlanFlowDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    [Fact]
    public async Task Migrate_ExistingConnection_HasNoFullPassYet_SoItsNextSyncIsOne()
    {
        await using var db = NewContext();
        await db.Database.OpenConnectionAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica"); // the user does not exist; skip FK checks
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO calendar_integrations (\"Id\", \"CreatedAtUtc\", \"UserId\", \"Provider\", \"ExternalAccountId\", \"ExternalAccountEmail\", " +
            "\"EncryptedAccessToken\", \"EncryptedRefreshToken\", \"AccessTokenExpiresAtUtc\", \"LastSyncedAtUtc\", \"IsActive\") " +
            $"VALUES ('{Guid.NewGuid()}', now(), '{Guid.NewGuid()}', 0, 'sub', 'a@example.com', 'x', 'y', now(), now(), true)");

        await migrator.MigrateAsync();

        var integration = await db.CalendarIntegrations.SingleAsync();
        Assert.NotNull(integration.LastSyncedAtUtc);
        Assert.Null(integration.LastFullSyncAtUtc);
    }

    [Fact]
    public async Task ItemReminders_AcceptEitherFormButNotBothOrNeither_AndGoWithTheirItem()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica"); // team and user are not needed for this check

        var item = new CalendarItem { TeamId = Guid.NewGuid(), Title = "Holiday", TaskType = TaskType.Event, Status = TaskStatus.Confirmed };
        item.Reminders.Add(ItemReminder.Offset(30));
        item.Reminders.Add(ItemReminder.Day(1, 540));
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.ItemReminders.CountAsync());

        // Both forms at once, neither form, and a time of day outside 0-1439 are all refused by the database itself.
        foreach (var (offset, days, timeOfDay) in new (string, string, string)[]
                 {
                     ("30", "1", "540"),
                     ("NULL", "NULL", "NULL"),
                     ("NULL", "1", "1440"),
                     ("NULL", "1", "NULL")
                 })
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO item_reminders (\"Id\", \"CreatedAtUtc\", \"CalendarItemId\", \"OffsetMinutes\", \"DaysBefore\", \"TimeOfDayMinutes\") " +
                $"VALUES ('{Guid.NewGuid()}', now(), '{item.Id}', {offset}, {days}, {timeOfDay})"));
            Assert.Equal("23514", exception.SqlState); // check_violation
        }

        db.CalendarItems.Remove(item);
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.ItemReminders.CountAsync());
    }
}
