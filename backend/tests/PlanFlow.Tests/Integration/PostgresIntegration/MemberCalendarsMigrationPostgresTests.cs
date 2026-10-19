using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The 7.5.10 migration adds Google's privacy flag to every item. Existing items come through with "unknown" (null, read as private),
/// and every integration forgets its last full pass so the next sync fills the flag in. Checked on a real PostgreSQL database.
/// </summary>
public class MemberCalendarsMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261018230159_OwnItemLists";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_member_calendars_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrate_KeepsItems_WithUnknownPrivacy_AndForcesTheNextFullSyncPass()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync($"INSERT INTO teams (\"Id\", \"CreatedAtUtc\", \"Name\", \"IsPersonal\") VALUES ('{teamId}', now(), 'T', false)");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO users (\"Id\", \"CreatedAtUtc\", \"DisplayName\", \"Email\", \"PasswordHash\", \"CalendarSharingLevel\") " +
            $"VALUES ('{userId}', now(), 'Anna', '{userId:N}@example.com', 'x', 1)");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO calendar_integrations (\"Id\", \"CreatedAtUtc\", \"AccessTokenExpiresAtUtc\", \"EncryptedAccessToken\", \"EncryptedRefreshToken\", " +
            "\"ExternalAccountEmail\", \"ExternalAccountId\", \"GrantedScopes\", \"IsActive\", \"LastFullSyncAtUtc\", \"LastSyncedAtUtc\", \"Provider\", \"UserId\") " +
            $"VALUES ('{integrationId}', now(), now(), 'a', 'r', 'a@example.com', 'acc', 'calendar', true, now(), now(), 0, '{userId}')");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO calendar_items (\"Id\", \"CreatedAtUtc\", \"TeamId\", \"Title\", \"Status\", \"TaskType\", \"EventTemplate\", \"IsBusy\", \"IsAllDay\", " +
            "\"IsDeadlineDefaulted\", \"IsAssignmentLocked\", \"IsRecurring\", \"UseAiEnhancement\", \"IsDataIncomplete\", \"CurrentUrgencyScore\") " +
            $"VALUES ('{itemId}', now(), '{teamId}', 'Old event', 0, 1, 0, true, false, false, false, false, false, false, 0)");

        await migrator.MigrateAsync();

        var item = await db.CalendarItems.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal("Old event", item.Title);
        Assert.Null(item.IsPrivate);

        var integration = await db.CalendarIntegrations.AsNoTracking().SingleAsync(i => i.Id == integrationId);
        Assert.Null(integration.LastFullSyncAtUtc);
        Assert.NotNull(integration.LastSyncedAtUtc); // only the full-pass marker is reset
    }
}
