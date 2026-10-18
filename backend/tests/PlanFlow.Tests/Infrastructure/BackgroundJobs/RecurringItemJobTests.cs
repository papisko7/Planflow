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

/// <summary>Recurring items: finishing one spawns the next instance, until the end of the cycle is reached.</summary>
public class RecurringItemJobTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static Mock<IJobExecutionContext> JobContext()
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private static RecurringItemJob Job(PlanFlowDbContext db) =>
        new(db, Mock.Of<ICacheService>(), Mock.Of<Microsoft.Extensions.Logging.ILogger<RecurringItemJob>>());

    private static async Task<(PlanFlowDbContext Db, CalendarItem Item)> SetUpAsync(Action<CalendarItem> configure)
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (user, TeamRole.Owner));
        var item = new CalendarItem
        {
            TeamId = team.Id, AssignedUserId = user.Id, Title = "Weekly report", Status = TaskStatus.Completed,
            IsRecurring = true, RecurrenceIntervalHours = 24 * 7, DeadlineUtc = Now.AddDays(1), ImpactScore = 6
        };
        configure(item);
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();
        return (db, item);
    }

    [Fact]
    public async Task Execute_FinishedRecurringTask_CreatesNextInstanceShiftedByTheInterval()
    {
        var (db, item) = await SetUpAsync(_ => { });
        var oldDeadline = item.DeadlineUtc!.Value;

        await Job(db).Execute(JobContext().Object);

        var next = Assert.Single(db.CalendarItems, t => t.Id != item.Id);
        Assert.Equal(oldDeadline.AddDays(7), next.DeadlineUtc);
        Assert.Equal(TaskStatus.Backlog, next.Status);
        Assert.Equal("Weekly report", next.Title);
        Assert.Equal(item.AssignedUserId, next.AssignedUserId);
        Assert.Equal(6, next.ImpactScore);
        Assert.True(next.IsRecurring);
        Assert.Equal(item.RecurrenceIntervalHours, next.RecurrenceIntervalHours);
        Assert.False(item.IsRecurring); // the finished one is closed, the chain lives on in the new item
        Assert.True(next.CurrentUrgencyScore > 0);
    }

    [Fact]
    public async Task Execute_RunTwice_DoesNotCreateDuplicates()
    {
        var (db, _) = await SetUpAsync(_ => { });

        await Job(db).Execute(JobContext().Object);
        await Job(db).Execute(JobContext().Object);

        Assert.Equal(2, db.CalendarItems.Count());
    }

    [Fact]
    public async Task Execute_NewDeadlineBeyondCycleEnd_CreatesNothingAndClosesTheChain()
    {
        var (db, item) = await SetUpAsync(t => t.RecurrenceEndsAtUtc = Now.AddDays(3)); // next deadline is in 8 days

        await Job(db).Execute(JobContext().Object);

        Assert.Single(db.CalendarItems);
        Assert.False(item.IsRecurring);
    }

    [Fact]
    public async Task Execute_CycleAlreadyEnded_CreatesNothing()
    {
        var (db, item) = await SetUpAsync(t =>
        {
            t.DeadlineUtc = Now.AddDays(-10);
            t.RecurrenceEndsAtUtc = Now.AddDays(-1);
        });

        await Job(db).Execute(JobContext().Object);

        Assert.Single(db.CalendarItems);
        Assert.False(item.IsRecurring);
    }

    [Fact]
    public async Task Execute_NewDeadlineExactlyAtCycleEnd_IsStillCreated()
    {
        var (db, item) = await SetUpAsync(t => t.RecurrenceEndsAtUtc = t.DeadlineUtc!.Value.AddDays(7));

        await Job(db).Execute(JobContext().Object);

        Assert.Equal(2, db.CalendarItems.Count());
    }

    [Fact]
    public async Task Execute_FinishedMuchLater_SkipsMissedOccurrencesSoTheNewDeadlineIsInTheFuture()
    {
        // Daily item due 10.5 days ago: shifting by one day would still be overdue.
        var (db, item) = await SetUpAsync(t =>
        {
            t.RecurrenceIntervalHours = 24;
            t.DeadlineUtc = Now.AddDays(-10.5);
        });

        await Job(db).Execute(JobContext().Object);

        var next = Assert.Single(db.CalendarItems, t => t.Id != item.Id);
        Assert.True(next.DeadlineUtc > DateTime.UtcNow);
        Assert.True(next.DeadlineUtc <= DateTime.UtcNow.AddHours(24)); // the nearest future slot, not further
    }

    // An Event has no "completed" status: it counts as finished once it is over, then its next occurrence is created.
    private static Task<(PlanFlowDbContext Db, CalendarItem Item)> SetUpEventAsync(TimeSpan startOffset, TaskStatus status = TaskStatus.Confirmed) =>
        SetUpAsync(t =>
        {
            t.TaskType = TaskType.Event;
            t.Status = status;
            t.DeadlineUtc = null;
            t.StartUtc = Now + startOffset;
            t.EndUtc = Now + startOffset + TimeSpan.FromHours(1);
        });

    [Fact]
    public async Task Execute_RecurringEventThatIsOver_ShiftsStartAndEndTogether()
    {
        var (db, item) = await SetUpEventAsync(TimeSpan.FromHours(-3));

        await Job(db).Execute(JobContext().Object);

        var next = Assert.Single(db.CalendarItems, t => t.Id != item.Id);
        Assert.Equal(item.StartUtc!.Value.AddDays(7), next.StartUtc);
        Assert.Equal(item.EndUtc!.Value.AddDays(7), next.EndUtc);
        Assert.Null(next.DeadlineUtc);
        Assert.Equal(TaskStatus.Confirmed, next.Status); // a new event starts Confirmed, not as a task in the backlog
        Assert.False(item.IsRecurring);
    }

    [Fact]
    public async Task Execute_RecurringEventNotOverYet_IsLeftAlone()
    {
        var (db, item) = await SetUpEventAsync(TimeSpan.FromHours(2));

        await Job(db).Execute(JobContext().Object);

        Assert.Single(db.CalendarItems);
        Assert.True(item.IsRecurring);
    }

    [Fact]
    public async Task Execute_CancelledRecurringEventThatIsOver_IsLeftAlone()
    {
        var (db, item) = await SetUpEventAsync(TimeSpan.FromHours(-3), TaskStatus.Cancelled);

        await Job(db).Execute(JobContext().Object);

        Assert.Single(db.CalendarItems);
        Assert.True(item.IsRecurring);
    }

    [Theory]
    [InlineData(TaskStatus.Backlog)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.Cancelled)]
    public async Task Execute_ItemNotDone_IsLeftAlone(TaskStatus status)
    {
        var (db, item) = await SetUpAsync(t => t.Status = status);

        await Job(db).Execute(JobContext().Object);

        Assert.Single(db.CalendarItems);
        Assert.True(item.IsRecurring);
    }

    [Fact]
    public void CreateNextInstance_DoesNotCopyBlockerOrExternalCalendarIds()
    {
        var finished = new CalendarItem
        {
            IsRecurring = true, RecurrenceIntervalHours = 24, DeadlineUtc = Now.AddHours(1), Status = TaskStatus.Completed,
            BlockedByTaskId = Guid.NewGuid(), ExternalCalendarEventId = "gcal-1", SourceCalendarIntegrationId = Guid.NewGuid()
        };

        var next = RecurrenceScheduler.CreateNextInstance(finished, Now)!;

        Assert.Null(next.BlockedByTaskId);
        Assert.Null(next.ExternalCalendarEventId);
        Assert.Null(next.SourceCalendarIntegrationId);
    }
}
