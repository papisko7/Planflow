using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The migration adds the team's own lists and a nullable list link on every item. Items that already exist must come through
/// untouched (no list), and deleting a list must keep its items. Checked on a real PostgreSQL database.
/// </summary>
public class OwnItemListsMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261018205857_ItemAlertsAndParticipants";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_own_lists_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrate_KeepsExistingItems_WithNoList()
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
            "\"IsDeadlineDefaulted\", \"IsAssignmentLocked\", \"IsRecurring\", \"UseAiEnhancement\", \"IsDataIncomplete\", \"CurrentUrgencyScore\") " +
            $"VALUES ('{itemId}', now(), '{teamId}', 'Old item', 0, 0, 0, true, false, false, false, false, false, false, 0)");

        await migrator.MigrateAsync();

        var item = await db.CalendarItems.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal("Old item", item.Title);
        Assert.Null(item.ItemListId);
    }

    [Fact]
    public async Task DeletingAList_KeepsItsItems_AndAListGoesWithItsTeam()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.GetService<IMigrator>().MigrateAsync();

        var team = new Team { Name = "T" };
        db.Teams.Add(team);
        var list = new ItemList { TeamId = team.Id, Name = "Sprint", Kind = ItemListKind.TaskList };
        db.ItemLists.Add(list);
        var item = new CalendarItem { TeamId = team.Id, Title = "In a list", TaskType = TaskType.Task, Status = TaskStatus.Backlog, DeadlineUtc = DateTime.UtcNow.AddDays(2), ItemListId = list.Id };
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync($"DELETE FROM item_lists WHERE \"Id\" = '{list.Id}'");
        db.ChangeTracker.Clear();
        var kept = await db.CalendarItems.SingleAsync(i => i.Id == item.Id);
        Assert.Null(kept.ItemListId);

        var second = new ItemList { TeamId = team.Id, Name = "Other", Kind = ItemListKind.MeetingList };
        db.ItemLists.Add(second);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM teams WHERE \"Id\" = '{team.Id}'");
        Assert.Equal(0, await db.ItemLists.CountAsync());
    }
}
