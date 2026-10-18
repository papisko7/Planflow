using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Common;

/// <summary>
/// The recalculator is what keeps stored scores in step with reality: one pass rescores the whole team,
/// writes an audit row only when something changed (or for the task the user touched), and ignores finished tasks.
/// </summary>
public class TeamUrgencyRecalculatorTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<(PlanFlowDbContext Db, Guid TeamId)> CreateTeamAsync()
    {
        var db = TestDb.Create();
        var team = await TestDb.AddTeamAsync(db);
        return (db, team.Id);
    }

    private static CalendarItem AddTask(PlanFlowDbContext db, Guid teamId, DateTime? deadline = null, int? impact = 5,
        Guid? blockedBy = null, TaskStatus status = TaskStatus.Backlog)
    {
        var task = new CalendarItem
        {
            TeamId = teamId, Title = "t", DeadlineUtc = deadline, ImpactScore = impact,
            BlockedByTaskId = blockedBy, Status = status
        };
        db.CalendarItems.Add(task);
        return task;
    }

    [Fact]
    public async Task RecalculateTeam_RescoresEveryActiveTaskAndLogsTheChange()
    {
        var (db, teamId) = await CreateTeamAsync();
        var task = AddTask(db, teamId, deadline: Now.AddDays(1), impact: 8);
        await db.SaveChangesAsync();

        var changed = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            db, teamId, Now, ScoreTriggerSource.ScheduledRecalculation, null, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Contains(task.Id, changed);
        Assert.True(task.CurrentUrgencyScore > 0.8);
        Assert.Single(db.UrgencyScoreLogs, l => l.CalendarItemId == task.Id);
    }

    [Fact]
    public async Task RecalculateTeam_NothingChanged_WritesNoNewAuditRows()
    {
        var (db, teamId) = await CreateTeamAsync();
        AddTask(db, teamId, deadline: Now.AddDays(10), impact: 3);
        await db.SaveChangesAsync();
        await TeamUrgencyRecalculator.RecalculateTeamAsync(db, teamId, Now, ScoreTriggerSource.ScheduledRecalculation, null, CancellationToken.None);
        await db.SaveChangesAsync();
        var logsAfterFirstPass = db.UrgencyScoreLogs.Count();

        var changed = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            db, teamId, Now, ScoreTriggerSource.ScheduledRecalculation, null, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Empty(changed);
        Assert.Equal(logsAfterFirstPass, db.UrgencyScoreLogs.Count());
    }

    [Fact]
    public async Task RecalculateTeam_TimePasses_ScoreOfUntouchedTaskRises()
    {
        var (db, teamId) = await CreateTeamAsync();
        var task = AddTask(db, teamId, deadline: Now.AddDays(5), impact: 3);
        await db.SaveChangesAsync();
        await TeamUrgencyRecalculator.RecalculateTeamAsync(db, teamId, Now, ScoreTriggerSource.ScheduledRecalculation, null, CancellationToken.None);
        var before = task.CurrentUrgencyScore;

        await TeamUrgencyRecalculator.RecalculateTeamAsync(
            db, teamId, Now.AddDays(4), ScoreTriggerSource.ScheduledRecalculation, null, CancellationToken.None);

        Assert.True(task.CurrentUrgencyScore > before);
    }

    [Fact]
    public async Task RecalculateTeam_NewSubjectNotYetInDatabase_IsScoredAndLoggedAlways()
    {
        var (db, teamId) = await CreateTeamAsync();
        var created = AddTask(db, teamId, impact: 0); // score stays 0, yet the touched task still gets its audit row

        var changed = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            db, teamId, Now, ScoreTriggerSource.ManualCreate, created, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Contains(created.Id, changed);
        Assert.Equal(ScoreTriggerSource.ManualCreate, db.UrgencyScoreLogs.Single(l => l.CalendarItemId == created.Id).TriggerSource);
    }

    [Fact]
    public async Task RecalculateTeam_NewDependentTask_RaisesItsBlockerInTheSamePass()
    {
        var (db, teamId) = await CreateTeamAsync();
        var blocker = AddTask(db, teamId, impact: 0);
        await db.SaveChangesAsync();
        var dependent = AddTask(db, teamId, deadline: Now.AddHours(4), impact: 8, blockedBy: blocker.Id);

        await TeamUrgencyRecalculator.RecalculateTeamAsync(
            db, teamId, Now, ScoreTriggerSource.ManualCreate, dependent, CancellationToken.None);

        Assert.True(blocker.CurrentUrgencyScore > 0.5);
        Assert.True(dependent.CurrentUrgencyScore < blocker.CurrentUrgencyScore);
    }

    [Fact]
    public async Task RecalculateTeam_FinishedBlocker_ReleasesTheBlockedTask()
    {
        var (db, teamId) = await CreateTeamAsync();
        var blocker = AddTask(db, teamId, impact: 0);
        var dependent = AddTask(db, teamId, deadline: Now.AddDays(2), impact: 8, blockedBy: blocker.Id);
        await db.SaveChangesAsync();
        await TeamUrgencyRecalculator.RecalculateTeamAsync(db, teamId, Now, ScoreTriggerSource.ManualUpdate, null, CancellationToken.None);
        var whileBlocked = dependent.CurrentUrgencyScore;

        blocker.Status = TaskStatus.Completed;
        await TeamUrgencyRecalculator.RecalculateTeamAsync(db, teamId, Now, ScoreTriggerSource.ManualUpdate, blocker, CancellationToken.None);

        Assert.True(dependent.CurrentUrgencyScore > whileBlocked);
        Assert.False(db.UrgencyScoreLogs.Local.Last(l => l.CalendarItemId == dependent.Id).IsBlocked);
    }

    [Fact]
    public async Task RecalculateTeam_FinishedTasks_AreNotRescoredByTheScheduledPass()
    {
        var (db, teamId) = await CreateTeamAsync();
        var done = AddTask(db, teamId, deadline: Now.AddDays(1), impact: 9, status: TaskStatus.Completed);
        await db.SaveChangesAsync();

        var changed = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            db, teamId, Now, ScoreTriggerSource.ScheduledRecalculation, null, CancellationToken.None);

        Assert.DoesNotContain(done.Id, changed);
        Assert.Equal(0.0, done.CurrentUrgencyScore);
    }

    // ---- Step 7.5.4 ----

    [Fact]
    public async Task RecalculateTeam_InformationalItem_KeepsAZeroScoreAndGetsNoAuditRow()
    {
        var (db, teamId) = await CreateTeamAsync();
        var leave = new CalendarItem
        {
            TeamId = teamId, Title = "Holiday", TaskType = TaskType.Event, EventTemplate = EventTemplate.OutOfOffice,
            StartUtc = Now.AddHours(1), EndUtc = Now.AddDays(3), ImpactScore = 10, Status = TaskStatus.Confirmed
        };
        db.CalendarItems.Add(leave);
        await db.SaveChangesAsync();

        await TeamUrgencyRecalculator.RecalculateTeamAsync(db, teamId, Now, ScoreTriggerSource.ManualCreate, leave, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(0.0, leave.CurrentUrgencyScore);
        Assert.Empty(db.UrgencyScoreLogs.Where(l => l.CalendarItemId == leave.Id));
    }

    [Fact]
    public async Task RecalculateTeam_InformationalItemScoredByTheOldAlgorithm_IsResetToZeroOnce()
    {
        var (db, teamId) = await CreateTeamAsync();
        var focus = new CalendarItem
        {
            TeamId = teamId, Title = "Deep work", TaskType = TaskType.Event, EventTemplate = EventTemplate.FocusTime,
            StartUtc = Now.AddHours(1), EndUtc = Now.AddHours(3), Status = TaskStatus.Confirmed, CurrentUrgencyScore = 0.91 // v1 scored it
        };
        db.CalendarItems.Add(focus);
        await db.SaveChangesAsync();

        var first = await TeamUrgencyRecalculator.RecalculateTeamAsync(db, teamId, Now, ScoreTriggerSource.ScheduledRecalculation, null, CancellationToken.None);
        var second = await TeamUrgencyRecalculator.RecalculateTeamAsync(db, teamId, Now, ScoreTriggerSource.ScheduledRecalculation, null, CancellationToken.None);

        Assert.Equal(0.0, focus.CurrentUrgencyScore);
        Assert.Contains(focus.Id, first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task RecalculateTeam_ReadsAnAllDayItemInTheAssigneesTimeZone()
    {
        var (db, teamId) = await CreateTeamAsync();
        var user = await TestDb.AddUserAsync(db, "Ann");
        user.TimeZoneId = "Europe/Warsaw";
        // Due on 5 Oct (the literal day): in Warsaw that day ends 5 Oct 22:00 UTC, so at 12:00 UTC there are 10 hours left, not 12.
        var task = AddTask(db, teamId, deadline: new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), impact: 5);
        task.IsAllDay = true;
        task.AssignedUserId = user.Id;
        await db.SaveChangesAsync();

        await TeamUrgencyRecalculator.RecalculateTeamAsync(db, teamId, Now, ScoreTriggerSource.ScheduledRecalculation, task, CancellationToken.None);

        var log = db.UrgencyScoreLogs.Local.Single(l => l.CalendarItemId == task.Id);
        Assert.Contains("due in 10h", log.ReasonText);
        Assert.Equal(UrgencyScoreCalculator.AlgorithmVersion, log.AlgorithmVersion);
    }

    [Fact]
    public async Task RecalculateTeam_OnlyTouchesTheGivenTeam()
    {
        var (db, teamId) = await CreateTeamAsync();
        var otherTeam = await TestDb.AddTeamAsync(db);
        var mine = AddTask(db, teamId, deadline: Now.AddDays(1), impact: 8);
        var theirs = AddTask(db, otherTeam.Id, deadline: Now.AddDays(1), impact: 8);
        await db.SaveChangesAsync();

        await TeamUrgencyRecalculator.RecalculateTeamAsync(
            db, teamId, Now, ScoreTriggerSource.ManualUpdate, null, CancellationToken.None);

        Assert.True(mine.CurrentUrgencyScore > 0.0);
        Assert.Equal(0.0, theirs.CurrentUrgencyScore);
    }
}
