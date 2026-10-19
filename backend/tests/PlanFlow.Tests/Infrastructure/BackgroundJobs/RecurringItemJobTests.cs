using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Application;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>
/// Repeating items: an event series is kept created 90 days ahead, a task series gets its next occurrence when one is
/// completed. The series' cursor is what stops a second run, or a deleted occurrence, from creating anything twice.
/// </summary>
public class RecurringItemJobTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static Mock<IJobExecutionContext> JobContext()
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private static Task RunAsync(PlanFlowDbContext db) =>
        new RecurringItemJob(db, Mock.Of<ICacheService>(), Mock.Of<Microsoft.Extensions.Logging.ILogger<RecurringItemJob>>()).Execute(JobContext().Object);

    private sealed record Setup(PlanFlowDbContext Db, Team Team, User User);

    private static async Task<Setup> SetUpAsync()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (user, TeamRole.Owner));
        return new Setup(db, team, user);
    }

    /// <summary>A daily event series whose rows (<paramref name="existingDays"/>, counted from tomorrow) exist and whose cursor is at <paramref name="cursorDays"/> from now.</summary>
    private static async Task<(RecurrenceSeries Series, List<CalendarItem> Rows)> EventSeriesAsync(
        Setup s, int cursorDays, int[] existingDays, DateTime? ends = null, Action<CalendarItem>? configure = null)
    {
        var anchor = Now.Date.AddDays(1).AddHours(9);
        var series = new RecurrenceSeries
        {
            TeamId = s.Team.Id, TaskType = TaskType.Event, Frequency = RecurrenceFrequency.Daily, Interval = 1, AnchorUtc = anchor,
            EndsAtUtc = ends, GeneratedThroughUtc = Now.AddDays(cursorDays)
        };
        s.Db.RecurrenceSeries.Add(series);
        var rows = existingDays.Select(day =>
        {
            var item = new CalendarItem
            {
                TeamId = s.Team.Id, AssignedUserId = s.User.Id, Title = "Standup", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
                StartUtc = anchor.AddDays(day), EndUtc = anchor.AddDays(day).AddMinutes(30), ImpactScore = 4, RecurrenceSeriesId = series.Id
            };
            configure?.Invoke(item);
            return item;
        }).ToList();
        s.Db.CalendarItems.AddRange(rows);
        await s.Db.SaveChangesAsync();
        return (series, rows);
    }

    [Fact]
    public async Task EventSeries_ReachingLessThan80DaysAhead_IsToppedUpToNinetyDays()
    {
        var s = await SetUpAsync();
        var (series, rows) = await EventSeriesAsync(s, cursorDays: 10, existingDays: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);

        await RunAsync(s.Db);

        var all = s.Db.CalendarItems.Where(i => i.RecurrenceSeriesId == series.Id).OrderBy(i => i.StartUtc).ToList();
        Assert.True(all.Count > rows.Count);
        Assert.True(all.Max(i => i.StartUtc) <= DateTime.UtcNow.AddDays(90));
        Assert.True(all.Max(i => i.StartUtc) > DateTime.UtcNow.AddDays(88)); // reaches (almost) the whole window
        Assert.True(series.GeneratedThroughUtc >= DateTime.UtcNow.AddDays(89));
        Assert.Equal(all.Count, all.Select(i => i.StartUtc).Distinct().Count()); // one occurrence per day
    }

    [Fact]
    public async Task EventSeries_NewOccurrence_KeepsTheLengthAndTheContentOfTheTemplate_AndIsScored()
    {
        var s = await SetUpAsync();
        var (series, rows) = await EventSeriesAsync(s, cursorDays: 5, existingDays: [0, 1, 2, 3, 4], configure: i =>
        {
            i.Location = "Room 4"; i.ColorKey = "tomato"; i.EstimatedEffortHours = 0.5;
        });

        await RunAsync(s.Db);

        var created = s.Db.CalendarItems.Where(i => i.RecurrenceSeriesId == series.Id && !rows.Contains(i)).First();
        Assert.Equal("Standup", created.Title);
        Assert.Equal("Room 4", created.Location);
        Assert.Equal("tomato", created.ColorKey);
        Assert.Equal(s.User.Id, created.AssignedUserId);
        Assert.Equal(TaskStatus.Confirmed, created.Status);
        Assert.Equal(TimeSpan.FromMinutes(30), created.EndUtc - created.StartUtc);
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task EventSeries_RunTwice_DoesNotCreateDuplicates()
    {
        var s = await SetUpAsync();
        await EventSeriesAsync(s, cursorDays: 10, existingDays: [0, 1, 2]);

        await RunAsync(s.Db);
        var afterFirst = s.Db.CalendarItems.Count();
        await RunAsync(s.Db);

        Assert.Equal(afterFirst, s.Db.CalendarItems.Count());
    }

    [Fact]
    public async Task EventSeries_AnOccurrenceTheUserDeleted_IsNotMadeAgain()
    {
        // Days 3..29 are gone (deleted by the user); the cursor says everything up to day 30 was dealt with.
        var s = await SetUpAsync();
        var (series, _) = await EventSeriesAsync(s, cursorDays: 30, existingDays: [0, 1, 2]);

        await RunAsync(s.Db);

        var starts = s.Db.CalendarItems.Where(i => i.RecurrenceSeriesId == series.Id).Select(i => i.StartUtc!.Value).ToList();
        var tomorrow9 = Now.Date.AddDays(1).AddHours(9);
        Assert.DoesNotContain(starts, d => d > tomorrow9.AddDays(2) && d <= Now.AddDays(30));
        Assert.Contains(starts, d => d > Now.AddDays(30));
    }

    [Fact]
    public async Task EventSeries_AlreadyAhead_IsLeftAlone()
    {
        var s = await SetUpAsync();
        await EventSeriesAsync(s, cursorDays: 85, existingDays: [0, 1]);

        await RunAsync(s.Db);

        Assert.Equal(2, s.Db.CalendarItems.Count());
    }

    [Fact]
    public async Task EventSeries_StopsAtItsEnd_AndIsNotVisitedAgain()
    {
        var s = await SetUpAsync();
        var ends = Now.Date.AddDays(11).AddHours(9);
        var (series, _) = await EventSeriesAsync(s, cursorDays: 3, existingDays: [0, 1, 2], ends: ends);

        await RunAsync(s.Db);

        var last = s.Db.CalendarItems.Max(i => i.StartUtc);
        Assert.Equal(ends, last);
        Assert.True(series.GeneratedThroughUtc >= ends); // closed: the job's filter no longer picks it

        var count = s.Db.CalendarItems.Count();
        await RunAsync(s.Db);
        Assert.Equal(count, s.Db.CalendarItems.Count());
    }

    [Fact]
    public async Task EventSeries_NewOccurrences_CopyTheLatestOneNobodyChangedOnItsOwn()
    {
        var s = await SetUpAsync();
        var (series, rows) = await EventSeriesAsync(s, cursorDays: 5, existingDays: [0, 1, 2, 3, 4]);
        rows[4].Title = "Standup (moved to the garden, this once)";
        rows[4].IsSeriesException = true;
        await s.Db.SaveChangesAsync();

        await RunAsync(s.Db);

        var created = s.Db.CalendarItems.Where(i => i.RecurrenceSeriesId == series.Id && !rows.Contains(i)).ToList();
        Assert.NotEmpty(created);
        Assert.All(created, c => Assert.Equal("Standup", c.Title));
    }

    [Fact]
    public async Task EventSeries_NewOccurrences_CarryTheAlertsAndTheGuests_ButNotTheMemoryOfHavingGoneOff()
    {
        var s = await SetUpAsync();
        var ben = await TestDb.AddUserAsync(s.Db, "Ben");
        s.Db.TeamMembers.Add(new TeamMember { TeamId = s.Team.Id, UserId = ben.Id, Role = TeamRole.Member });
        var (series, rows) = await EventSeriesAsync(s, cursorDays: 5, existingDays: [0, 1, 2, 3, 4]);
        var template = rows[4];
        s.Db.ItemReminders.AddRange(
            new ItemReminder { CalendarItemId = template.Id, OffsetMinutes = 30, LastFiredForUtc = Now.AddHours(-1) },
            new ItemReminder { CalendarItemId = template.Id, DaysBefore = 1, TimeOfDayMinutes = 540 });
        s.Db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = template.Id, UserId = ben.Id, Role = ParticipantRole.Guest });
        await s.Db.SaveChangesAsync();

        await RunAsync(s.Db);

        var next = s.Db.CalendarItems.Where(i => i.RecurrenceSeriesId == series.Id && !rows.Contains(i)).OrderBy(i => i.StartUtc).First();
        var reminders = s.Db.ItemReminders.Where(r => r.CalendarItemId == next.Id).ToList();
        Assert.Equal(2, reminders.Count);
        Assert.Contains(reminders, r => r is { OffsetMinutes: 30, LastFiredForUtc: null });
        Assert.Contains(reminders, r => r is { DaysBefore: 1, TimeOfDayMinutes: 540 });
        var guest = Assert.Single(s.Db.ItemParticipants, p => p.CalendarItemId == next.Id);
        Assert.Equal(ben.Id, guest.UserId);
        Assert.Equal(ParticipantRole.Guest, guest.Role);
    }

    // ---- task series: one open occurrence, the next one after completing it ----

    private static async Task<(RecurrenceSeries Series, CalendarItem Item)> TaskSeriesAsync(
        Setup s, TaskStatus status, double deadlineInDays, RecurrenceFrequency frequency = RecurrenceFrequency.Weekly, DateTime? ends = null)
    {
        var deadline = Now.AddDays(deadlineInDays);
        var series = new RecurrenceSeries
        {
            TeamId = s.Team.Id, TaskType = TaskType.Task, Frequency = frequency, Interval = 1, AnchorUtc = deadline, EndsAtUtc = ends, GeneratedThroughUtc = deadline
        };
        var item = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = s.User.Id, Title = "Weekly report", Status = status, DeadlineUtc = deadline, ImpactScore = 6,
            RecurrenceSeriesId = series.Id
        };
        s.Db.RecurrenceSeries.Add(series);
        s.Db.CalendarItems.Add(item);
        await s.Db.SaveChangesAsync();
        return (series, item);
    }

    [Fact]
    public async Task TaskSeries_CompletedOccurrence_GetsTheNextOneAWeekLater()
    {
        var s = await SetUpAsync();
        var (series, item) = await TaskSeriesAsync(s, TaskStatus.Completed, deadlineInDays: 1);
        var oldDeadline = item.DeadlineUtc!.Value;

        await RunAsync(s.Db);

        var next = Assert.Single(s.Db.CalendarItems, t => t.Id != item.Id);
        Assert.Equal(oldDeadline.AddDays(7), next.DeadlineUtc);
        Assert.Equal(TaskStatus.Backlog, next.Status);
        Assert.Equal("Weekly report", next.Title);
        Assert.Equal(6, next.ImpactScore);
        Assert.Equal(series.Id, next.RecurrenceSeriesId);
        Assert.True(next.CurrentUrgencyScore > 0);
        Assert.Equal(next.DeadlineUtc, series.GeneratedThroughUtc);
    }

    [Fact]
    public async Task TaskSeries_RunTwice_DoesNotCreateDuplicates()
    {
        var s = await SetUpAsync();
        await TaskSeriesAsync(s, TaskStatus.Completed, deadlineInDays: 1);

        await RunAsync(s.Db);
        await RunAsync(s.Db);

        Assert.Equal(2, s.Db.CalendarItems.Count());
    }

    [Fact]
    public async Task TaskSeries_FinishedMuchLater_SkipsMissedOccurrencesSoTheNewDeadlineIsInTheFuture()
    {
        var s = await SetUpAsync();
        var (_, item) = await TaskSeriesAsync(s, TaskStatus.Completed, deadlineInDays: -10.5, frequency: RecurrenceFrequency.Daily);

        await RunAsync(s.Db);

        var next = Assert.Single(s.Db.CalendarItems, t => t.Id != item.Id);
        Assert.True(next.DeadlineUtc > DateTime.UtcNow);
        Assert.True(next.DeadlineUtc <= DateTime.UtcNow.AddHours(24)); // the nearest future day, not further
    }

    [Fact]
    public async Task TaskSeries_NextOccurrenceBeyondTheEnd_CreatesNothingAndClosesTheSeries()
    {
        var s = await SetUpAsync();
        var (series, _) = await TaskSeriesAsync(s, TaskStatus.Completed, deadlineInDays: 1, ends: Now.AddDays(3)); // the next one would be in 8 days

        await RunAsync(s.Db);

        Assert.Single(s.Db.CalendarItems);
        Assert.Equal(series.EndsAtUtc, series.GeneratedThroughUtc);
        await RunAsync(s.Db);
        Assert.Single(s.Db.CalendarItems);
    }

    [Fact]
    public async Task TaskSeries_NextDeadlineExactlyAtTheEnd_IsStillCreated()
    {
        var s = await SetUpAsync();
        await TaskSeriesAsync(s, TaskStatus.Completed, deadlineInDays: 1, ends: Now.AddDays(8));

        await RunAsync(s.Db);

        Assert.Equal(2, s.Db.CalendarItems.Count());
    }

    [Theory]
    [InlineData(TaskStatus.Backlog)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.Cancelled)]
    public async Task TaskSeries_OccurrenceNotDone_IsLeftAlone(TaskStatus status)
    {
        var s = await SetUpAsync();
        await TaskSeriesAsync(s, status, deadlineInDays: 1);

        await RunAsync(s.Db);

        Assert.Single(s.Db.CalendarItems);
    }

    [Fact]
    public async Task TaskSeries_NextOccurrence_StaysWithTheSameAssigneeOnTheSameTerms_AndDropsTheBlocker()
    {
        var s = await SetUpAsync();
        var (_, item) = await TaskSeriesAsync(s, TaskStatus.Completed, deadlineInDays: 1);
        item.AssignedByUserId = Guid.NewGuid();
        item.IsAssignmentLocked = true;
        item.BlockedByTaskId = null;
        await s.Db.SaveChangesAsync();

        await RunAsync(s.Db);

        var next = Assert.Single(s.Db.CalendarItems, t => t.Id != item.Id);
        Assert.Equal(item.AssignedUserId, next.AssignedUserId);
        Assert.Equal(item.AssignedByUserId, next.AssignedByUserId);
        Assert.True(next.IsAssignmentLocked);
        Assert.Null(next.BlockedByTaskId);
        Assert.Null(next.ExternalCalendarEventId);
    }

    // ---- housekeeping ----

    [Fact]
    public async Task SeriesWithNoOccurrencesLeft_IsRemoved()
    {
        var s = await SetUpAsync();
        var (series, rows) = await EventSeriesAsync(s, cursorDays: 85, existingDays: [0]);
        s.Db.CalendarItems.RemoveRange(rows);
        await s.Db.SaveChangesAsync();

        await RunAsync(s.Db);

        Assert.DoesNotContain(s.Db.RecurrenceSeries, x => x.Id == series.Id);
    }

    [Fact]
    public async Task HourlyRescoring_SkipsOccurrencesMoreThanTwoWeeksAway_ButNotTheNearOnes()
    {
        var s = await SetUpAsync();
        var (_, rows) = await EventSeriesAsync(s, cursorDays: 85, existingDays: [0, 30], configure: i => i.CurrentUrgencyScore = 0.123);

        var cache = Mock.Of<ICacheService>();
        await new PrioritizationJob(s.Db, cache, Mock.Of<Microsoft.Extensions.Logging.ILogger<PrioritizationJob>>()).Execute(JobContext().Object);

        Assert.NotEqual(0.123, rows[0].CurrentUrgencyScore); // tomorrow: rescored
        Assert.Equal(0.123, rows[1].CurrentUrgencyScore);    // a month away: left as it was
    }
}
