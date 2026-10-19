using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Common;

/// <summary>
/// Step 7.5.10b: the assignee's out-of-office time is taken off a task's deadline (capped at 7 days) before the deadline curve is read, and
/// the explanation says so. What counts as an absence follows what the person shares.
/// </summary>
public class AbsenceAwareUrgencyTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);

    private static AbsenceSpan Away(double fromDays, double toDays) => new(Now.AddDays(fromDays), Now.AddDays(toDays));

    private static CalendarItem Task(double dueInDays, double? effortHours = null) => new()
    {
        TeamId = Guid.NewGuid(), Title = "Report", TaskType = TaskType.Task, Status = TaskStatus.Backlog,
        DeadlineUtc = Now.AddDays(dueInDays), EstimatedEffortHours = effortHours, ImpactScore = 5
    };

    private static UrgencyScoreLog Score(CalendarItem task, params AbsenceSpan[] absences) =>
        CalendarItemUrgencyScoreFactory.BuildScoreLog(task, 0.0, false, Now, absences: absences);

    // ---- the score ----

    [Fact]
    public void AbsenceBeforeTheDeadline_MakesTheDeadlineCloser()
    {
        var task = Task(5);

        var plain = Score(task);
        var away = Score(task, Away(2, 4));

        Assert.Equal(Math.Exp(-5.0 / 7.0), plain.DeadlineComponent, 6);
        Assert.Equal(Math.Exp(-3.0 / 7.0), away.DeadlineComponent, 6); // 5 days minus the 2 days away
        Assert.True(away.FinalScore > plain.FinalScore);
    }

    [Fact]
    public void AbsenceThatCoversTheRestOfTheTime_PutsTheDeadlineAtNow()
    {
        var log = Score(Task(3), Away(0, 3));

        Assert.Equal(1.0, log.DeadlineComponent);
        Assert.Contains("not enough time: only 3d left (3 d out of office)", log.ReasonText);
    }

    [Fact]
    public void AMonthOfLeave_TakesOffAtMostSevenDays()
    {
        var log = Score(Task(20), Away(1, 30));

        Assert.Equal(Math.Exp(-13.0 / 7.0), log.DeadlineComponent, 6);
        Assert.Contains("(7 d out of office)", log.ReasonText);
    }

    [Fact]
    public void AbsenceAfterTheDeadlineOrNoAbsence_ChangesNothing()
    {
        var task = Task(5);

        var plain = Score(task);
        var later = Score(task, Away(6, 9));

        Assert.Equal(plain.FinalScore, later.FinalScore);
        Assert.Equal(plain.ReasonText, later.ReasonText);
        Assert.DoesNotContain("out of office", later.ReasonText);
    }

    [Fact]
    public void AbsenceAndEffortAddUp()
    {
        var log = Score(Task(1.25, effortHours: 6), Away(0.25, 1.25)); // due in 30h, 6h of work, away for the last 24h: no time left

        Assert.Contains("not enough time: needs 6h, only 30h left (1 d out of office)", log.ReasonText);
        Assert.Equal(1.0, log.DeadlineComponent);
    }

    [Fact]
    public void PartOfADayAway_IsSaidInHours()
    {
        var log = Score(Task(4), Away(1, 1.25));

        Assert.Contains("due in 4d (6h out of office)", log.ReasonText);
    }

    [Fact]
    public void AnEvent_IsNotShortenedByTheAbsence()
    {
        var meeting = new CalendarItem
        {
            TeamId = Guid.NewGuid(), Title = "Standup", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            StartUtc = Now.AddDays(5), EndUtc = Now.AddDays(5).AddHours(1), ImpactScore = 5
        };

        Assert.Equal(Score(meeting).FinalScore, Score(meeting, Away(2, 4)).FinalScore);
    }

    [Fact]
    public void TheTeamPass_ReadsTheAbsencesOfEachAssignee()
    {
        var anna = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var annasTask = Task(5);
        annasTask.AssignedUserId = anna;
        var bobsTask = Task(5);
        bobsTask.AssignedUserId = bob;
        var absences = new Dictionary<Guid, IReadOnlyList<AbsenceSpan>> { [anna] = new[] { Away(1, 4) } };

        var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(
            new[] { annasTask, bobsTask }, Now, ScoreTriggerSource.ScheduledRecalculation, absencesOf: UserAbsences.ResolverFor(absences));

        Assert.True(logs[annasTask.Id].FinalScore > logs[bobsTask.Id].FinalScore);
        Assert.Contains("(3 d out of office)", logs[annasTask.Id].ReasonText);
        Assert.DoesNotContain("out of office", logs[bobsTask.Id].ReasonText);
    }

    // ---- what counts as an absence ----

    private sealed record Setup(PlanFlowDbContext Db, Team Team, User Owner, User Anna, Team AnnaPersonal);

    private static async Task<Setup> SetUpAsync(CalendarSharingLevel annaLevel = CalendarSharingLevel.Availability)
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var anna = await TestDb.AddUserAsync(db, "Anna");
        anna.CalendarSharingLevel = annaLevel;
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (anna, TeamRole.Member));
        var personal = new Team { Name = "Personal", IsPersonal = true };
        db.Teams.Add(personal);
        db.TeamMembers.Add(new TeamMember { TeamId = personal.Id, UserId = anna.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();
        return new Setup(db, team, owner, anna, personal);
    }

    private static CalendarItem AnnasTask(Setup s)
    {
        var task = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = s.Anna.Id, Title = "Report", TaskType = TaskType.Task, Status = TaskStatus.Backlog,
            DeadlineUtc = Now.AddDays(6), ImpactScore = 5
        };
        s.Db.CalendarItems.Add(task);
        s.Db.SaveChanges();
        return task;
    }

    private static void AddOutOfOffice(Setup s, Team team, bool imported, bool? isPrivate, double fromDays = 2, double toDays = 4)
    {
        s.Db.CalendarItems.Add(new CalendarItem
        {
            TeamId = team.Id, AssignedUserId = s.Anna.Id, Title = "Holiday", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            EventTemplate = EventTemplate.OutOfOffice, StartUtc = Now.AddDays(fromDays), EndUtc = Now.AddDays(toDays), IsPrivate = isPrivate,
            ExternalCalendarEventId = imported ? Guid.NewGuid().ToString() : null
        });
        s.Db.SaveChanges();
    }

    private static async Task<IReadOnlyList<AbsenceSpan>?> LoadForAnna(Setup s)
    {
        var all = await UserAbsences.LoadAsync(s.Db, s.Db.CalendarItems.ToList(), Now, CancellationToken.None);
        return all.GetValueOrDefault(s.Anna.Id);
    }

    [Fact]
    public async Task OutOfOfficeEventOfTheTeam_Counts()
    {
        var s = await SetUpAsync();
        AnnasTask(s);
        AddOutOfOffice(s, s.Team, imported: false, isPrivate: null);

        var spans = await LoadForAnna(s);

        var span = Assert.Single(spans!);
        Assert.Equal(Now.AddDays(2), span.StartUtc);
        Assert.Equal(Now.AddDays(4), span.EndUtc);
    }

    [Fact]
    public async Task ImportedOutOfOffice_CountsWhenSharedAndPublic()
    {
        var s = await SetUpAsync(CalendarSharingLevel.Availability);
        AnnasTask(s);
        AddOutOfOffice(s, s.AnnaPersonal, imported: true, isPrivate: false);

        Assert.Single((await LoadForAnna(s))!);
    }

    [Theory]
    [InlineData(CalendarSharingLevel.Off, false)]
    [InlineData(CalendarSharingLevel.Availability, true)] // marked private in Google: only ever "Busy", so no absence is revealed
    [InlineData(CalendarSharingLevel.Availability, null)] // privacy not known yet counts as private
    public async Task ImportedOutOfOffice_NotSharedOrPrivate_IsNotAnAbsenceForTheScore(CalendarSharingLevel level, bool? isPrivate)
    {
        var s = await SetUpAsync(level);
        AnnasTask(s);
        AddOutOfOffice(s, s.AnnaPersonal, imported: true, isPrivate: isPrivate);

        Assert.Null(await LoadForAnna(s));
    }

    [Fact]
    public async Task OnlyAssigneesOfOpenTasksWithAFutureDeadline_AreLooked_Up()
    {
        var s = await SetUpAsync();
        AddOutOfOffice(s, s.Team, imported: false, isPrivate: null);
        var done = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = s.Anna.Id, Title = "Old", TaskType = TaskType.Task, Status = TaskStatus.Backlog,
            DeadlineUtc = Now.AddDays(-1)
        };
        s.Db.CalendarItems.Add(done);
        s.Db.SaveChanges();

        var all = await UserAbsences.LoadAsync(s.Db, new[] { done }, Now, CancellationToken.None);

        Assert.Empty(all);
    }

    [Fact]
    public async Task ACancelledOrAlreadyFinishedOutOfOffice_IsIgnored()
    {
        var s = await SetUpAsync();
        AnnasTask(s);
        AddOutOfOffice(s, s.Team, imported: false, isPrivate: null, fromDays: -5, toDays: -3);
        AddOutOfOffice(s, s.Team, imported: false, isPrivate: null, fromDays: 1, toDays: 2);
        s.Db.CalendarItems.Single(i => i.StartUtc == Now.AddDays(1)).Status = TaskStatus.Cancelled;
        s.Db.SaveChanges();

        Assert.Null(await LoadForAnna(s));
    }

    // ---- through the real recalculation ----

    [Fact]
    public async Task RecalculatingTheTeam_UsesTheAssigneesTimeOff()
    {
        var s = await SetUpAsync();
        var task = AnnasTask(s);
        AddOutOfOffice(s, s.Team, imported: false, isPrivate: null, fromDays: 1, toDays: 5);
        var before = task.CurrentUrgencyScore;

        await TeamUrgencyRecalculator.RecalculateTeamAsync(s.Db, s.Team.Id, Now, ScoreTriggerSource.ManualUpdate, task, CancellationToken.None);
        await s.Db.SaveChangesAsync();

        Assert.True(task.CurrentUrgencyScore > before);
        var log = s.Db.UrgencyScoreLogs.Single(l => l.CalendarItemId == task.Id);
        Assert.Contains("(4 d out of office)", log.ReasonText);
    }
}
