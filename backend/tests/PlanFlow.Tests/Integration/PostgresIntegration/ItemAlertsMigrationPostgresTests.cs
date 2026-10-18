using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// The 7.5.6 migration adds the reminders switch to settings rows that already exist (EF would fill them with false; the migration is
/// edited so existing users get the same default as new ones), gives the reminders imported from Google an anchor (the start) and
/// creates the participants table. Checked on a real PostgreSQL database.
/// </summary>
public class ItemAlertsMigrationPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261018190635_GoogleTasksImport";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("planflow_alerts_migration_test")
        .WithUsername("planflow")
        .WithPassword("planflow_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrate_KeepsExistingData_AndGivesItTheNewDefaults()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync();

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        // Rows written before the new columns existed (the user and the item do not exist; skip FK checks for this session).
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO notification_preferences (\"Id\", \"CreatedAtUtc\", \"UserId\", \"EmailEnabled\", \"PushEnabled\", \"LoginAlerts\", " +
            "\"TaskAssignments\", \"TaskChanges\", \"TeamTasks\", \"TaskUrgency\", \"TeamMembership\", \"ChatDigest\", \"CalendarIssues\") " +
            $"VALUES ('{Guid.NewGuid()}', now(), '{Guid.NewGuid()}', true, true, 0, true, true, false, true, true, false, true)");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO item_reminders (\"Id\", \"CreatedAtUtc\", \"CalendarItemId\", \"OffsetMinutes\") " +
            $"VALUES ('{Guid.NewGuid()}', now(), '{Guid.NewGuid()}', 30)");

        await migrator.MigrateAsync();

        Assert.Equal(1, await Count(db, "SELECT count(*)::int AS \"Value\" FROM notification_preferences WHERE \"ItemReminders\""));
        Assert.Equal(1, await Count(db, "SELECT count(*)::int AS \"Value\" FROM item_reminders WHERE \"Anchor\" = 0 AND \"LastFiredForUtc\" IS NULL AND \"OffsetMinutes\" = 30"));
        Assert.Equal(0, await Count(db, "SELECT count(*)::int AS \"Value\" FROM item_participants"));
    }

    [Fact]
    public async Task Participants_OnePerPersonPerItem_AndTheyGoWithTheItem()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.GetService<IMigrator>().MigrateAsync();

        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Ben", PasswordHash = "x" };
        var team = new Team { Name = "T" };
        db.Users.Add(user);
        db.Teams.Add(team);
        var item = new CalendarItem { TeamId = team.Id, Title = "Retro", TaskType = TaskType.Event, Status = TaskStatus.Confirmed, StartUtc = DateTime.UtcNow.AddDays(1) };
        db.CalendarItems.Add(item);
        db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = item.Id, UserId = user.Id, Role = ParticipantRole.Guest });
        await db.SaveChangesAsync();

        // The same person twice on one item is refused by the database itself.
        db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = item.Id, UserId = user.Id, Role = ParticipantRole.CoContractor });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // Deleting the item takes its participants along.
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM calendar_items WHERE \"Id\" = '{item.Id}'");
        Assert.Equal(0, await Count(db, "SELECT count(*)::int AS \"Value\" FROM item_participants"));
    }

    [Fact]
    public async Task ReminderJobAndTeamList_QueriesRunOnRealPostgres()
    {
        // The job picks reminders with a CASE over the anchor and the team list nests the participants: both must translate to SQL.
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new PlanFlowDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.GetService<IMigrator>().MigrateAsync();

        var ann = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Ann", PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var ben = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Ben", PasswordHash = "x", EmailVerifiedAtUtc = DateTime.UtcNow };
        var team = new Team { Name = "T" };
        db.Users.AddRange(ann, ben);
        db.Teams.Add(team);
        db.TeamMembers.AddRange(
            new TeamMember { TeamId = team.Id, UserId = ann.Id, Role = TeamRole.Owner },
            new TeamMember { TeamId = team.Id, UserId = ben.Id, Role = TeamRole.Member });
        var start = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        var item = new CalendarItem { TeamId = team.Id, AssignedUserId = ann.Id, Title = "Retro", TaskType = TaskType.Event, Status = TaskStatus.Confirmed, StartUtc = start, EndUtc = start.AddHours(1) };
        var task = new CalendarItem { TeamId = team.Id, AssignedUserId = ann.Id, Title = "Report", TaskType = TaskType.Task, Status = TaskStatus.Backlog, DeadlineUtc = start.AddDays(1) };
        db.CalendarItems.AddRange(item, task);
        db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = item.Id, UserId = ben.Id, Role = ParticipantRole.Guest });
        db.ItemReminders.AddRange(
            new ItemReminder { CalendarItemId = item.Id, OffsetMinutes = 30 },
            new ItemReminder { CalendarItemId = task.Id, Anchor = ReminderAnchor.Deadline, OffsetMinutes = 60 });
        await db.SaveChangesAsync();

        var job = new PlanFlow.Infrastructure.BackgroundJobs.ItemReminderJob(
            db, new PlanFlow.Application.Notifications.NotificationService(db),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PlanFlow.Infrastructure.BackgroundJobs.ItemReminderJob>.Instance);

        // 09:30 UTC: the event's reminder (30 min before 10:00) is due, the task's (an hour before tomorrow's deadline) is not.
        var fired = await job.RunAsync(start.AddMinutes(-30), CancellationToken.None);

        Assert.Equal(1, fired);
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.Event == NotificationEvent.ItemReminder)); // the assignee and the guest

        var list = await new PlanFlow.Application.Tasks.Queries.GetTasksByTeam.GetTasksByTeamQueryHandler(
                db, new Moq.Mock<PlanFlow.Application.Common.Interfaces.ICacheService>().Object)
            .Handle(new PlanFlow.Application.Tasks.Queries.GetTasksByTeam.GetTasksByTeamQuery(team.Id), CancellationToken.None);
        Assert.Equal([ben.Id], list.Single(t => t.Id == item.Id).ParticipantUserIds);
        Assert.Empty(list.Single(t => t.Id == task.Id).ParticipantUserIds!);
    }

    private static async Task<int> Count(PlanFlowDbContext db, string sql) => (await db.Database.SqlQueryRaw<int>(sql).ToListAsync()).Single();
}
