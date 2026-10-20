using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Common;

/// <summary>
/// The deadline clock now depends on the item type and its effort estimate
/// (deadline of a Task / start of an Event, minus the effort), and the "imminent" floor window is dynamic.
/// </summary>
public class TaskTimingTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private static UrgencyScoreLog Score(CalendarItem task) =>
        CalendarItemUrgencyScoreFactory.BuildScoreLog(task, 0.0, false, Now);

    private static CalendarItem Item(TaskType type, DateTime? start = null, DateTime? deadline = null, double? effort = null, DateTime? end = null) => new()
    {
        Id = Guid.NewGuid(),
        TeamId = Guid.NewGuid(),
        Title = "T",
        TaskType = type,
        StartUtc = start,
        EndUtc = end,
        DeadlineUtc = deadline,
        EstimatedEffortHours = effort,
        ImpactScore = 0
    };

    // ---- Task: effectiveDeadline = deadline - effort ----

    [Fact]
    public void Task_WithoutEffort_BehavesLikeBeforeTheChange()
    {
        var task = Item(TaskType.Task, deadline: Now.AddDays(3));

        Assert.Equal(Math.Exp(-3.0 / 7.0), Score(task).DeadlineComponent, precision: 9);
    }

    [Fact]
    public void Task_Effort_ShiftsTheDeadlineEarlier()
    {
        var withEffort = Score(Item(TaskType.Task, deadline: Now.AddDays(3), effort: 24));
        var noEffort = Score(Item(TaskType.Task, deadline: Now.AddDays(3)));

        Assert.Equal(Math.Exp(-2.0 / 7.0), withEffort.DeadlineComponent, precision: 9);
        Assert.True(withEffort.DeadlineComponent > noEffort.DeadlineComponent);
    }

    [Fact]
    public void Task_EffortLongerThanTimeLeft_IsAlreadyOverdueAndGetsTheFloor()
    {
        // 10h of work, 8h left: it can no longer be finished in time.
        var log = Score(Item(TaskType.Task, deadline: Now.AddHours(8), effort: 10));

        Assert.Equal(1.0, log.DeadlineComponent);
        Assert.True(log.FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
    }

    [Fact]
    public void Task_SmallEffort_FloorStartsLaterThanTheDefault24h()
    {
        // 30 minutes of work due in 10h: no reason to panic yet (the old fixed rule would not either),
        // but a 30-minute task due in 40 minutes is.
        Assert.True(Score(Item(TaskType.Task, deadline: Now.AddHours(10), effort: 0.5)).FinalScore < UrgencyScoreCalculator.ImminentDeadlineFloor);
        Assert.True(Score(Item(TaskType.Task, deadline: Now.AddMinutes(20), effort: 0.5)).FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
    }

    [Fact]
    public void Task_LargeEffort_FloorStartsEarlierThanTheDefault24h()
    {
        var log = Score(Item(TaskType.Task, deadline: Now.AddHours(30), effort: 40));

        Assert.True(log.FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
    }

    // ---- Event: scored from its start ----

    [Fact]
    public void Event_IsScoredFromItsStart_NotFromItsEnd()
    {
        var evt = Item(TaskType.Event, start: Now.AddDays(1), end: Now.AddDays(5));

        Assert.Equal(Math.Exp(-1.0 / 7.0), Score(evt).DeadlineComponent, precision: 9);
    }

    [Fact]
    public void Event_WithoutStart_HasNoDeadlinePressure()
    {
        var evt = Item(TaskType.Event, deadline: Now.AddDays(2)); // an event has no deadline: a stray one is ignored

        Assert.Equal(0.0, Score(evt).DeadlineComponent);
    }

    [Fact]
    public void Event_Effort_ShiftsTheClockEarlier_LikeATasks()
    {
        var withEffort = Score(Item(TaskType.Event, start: Now.AddDays(2), effort: 24));
        var noEffort = Score(Item(TaskType.Event, start: Now.AddDays(2)));

        Assert.Equal(Math.Exp(-1.0 / 7.0), withEffort.DeadlineComponent, precision: 9);   // 2 days to the start, 1 day of work first
        Assert.Equal(Math.Exp(-2.0 / 7.0), noEffort.DeadlineComponent, precision: 9);
        Assert.True(withEffort.FinalScore > noEffort.FinalScore);
    }

    [Fact]
    public void Event_EffortLongerThanTimeLeft_IsAlreadyOverdueAndGetsTheFloor()
    {
        // 6h of preparation, the event starts in 4h: the work can no longer be done in time.
        var log = Score(Item(TaskType.Event, start: Now.AddHours(4), effort: 6));

        Assert.Equal(1.0, log.DeadlineComponent);
        Assert.True(log.FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
    }

    [Theory]
    [InlineData(null, 0.9, true)]   // default window 1h: starts in 54 minutes -> imminent
    [InlineData(null, 1.5, false)]  // ... starts in 90 minutes -> not yet
    [InlineData(1.5, 1.4, true)]    // 1.5h effort window
    [InlineData(10.0, 9.0, true)]   // the window is the effort itself, no longer capped at 2h
    [InlineData(10.0, 11.0, false)] // ... so 11h before is still outside it
    public void Event_FloorWindowIsItsEffortOrOneHourWhenUnknown(double? effort, double hoursToStart, bool expectedImminent)
    {
        var log = Score(Item(TaskType.Event, start: Now.AddHours(hoursToStart), effort: effort));

        Assert.Equal(expectedImminent, log.FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
    }

    [Fact]
    public void Event_AlreadyStarted_IsMaximallyUrgent()
    {
        var log = Score(Item(TaskType.Event, start: Now.AddMinutes(-10), end: Now.AddHours(1)));

        Assert.Equal(1.0, log.DeadlineComponent);
    }

    // ---- Past events are history, overdue tasks are not ----

    [Fact]
    public void Event_AfterItsEnd_DropsToZero()
    {
        var log = Score(Item(TaskType.Event, start: Now.AddHours(-3), end: Now.AddHours(-2)));

        Assert.Equal(0.0, log.FinalScore);
        Assert.Equal(0.0, log.DeadlineComponent);
    }

    [Fact]
    public void Event_AfterItsEnd_IgnoresEvenAManualOverrideAndHighImpact()
    {
        var evt = Item(TaskType.Event, start: Now.AddDays(-1), end: Now.AddDays(-1).AddHours(1));
        evt.ImpactScore = 10;
        evt.ManualUrgencyOverride = 1.0;

        Assert.Equal(0.0, Score(evt).FinalScore);
    }

    [Fact]
    public void Event_WhileItIsRunning_IsStillMaximallyUrgent_NotYetHistory()
    {
        var evt = Item(TaskType.Event, start: Now.AddMinutes(-30), end: Now.AddMinutes(30));

        Assert.True(Score(evt).FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
    }

    [Theory]
    [InlineData(-30, false)] // started 30 minutes ago, no end given -> assumed 1h long -> still running
    [InlineData(-90, true)]  // started 90 minutes ago -> the assumed hour is over -> history
    public void Event_WithoutAnEnd_IsAssumedToLastOneHour(int startMinutesFromNow, bool expectedPast)
    {
        var evt = Item(TaskType.Event, start: Now.AddMinutes(startMinutesFromNow));

        Assert.Equal(expectedPast, CalendarItemUrgencyScoreFactory.IsPastEvent(evt, Now));
        Assert.Equal(expectedPast, Score(evt).FinalScore == 0.0);
    }

    [Fact]
    public void Task_PastItsDeadline_KeepsTheMaximumScore()
    {
        var log = Score(Item(TaskType.Task, deadline: Now.AddDays(-5)));

        Assert.Equal(1.0, log.DeadlineComponent);
        Assert.True(log.FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
        Assert.False(CalendarItemUrgencyScoreFactory.IsPastEvent(Item(TaskType.Task, deadline: Now.AddDays(-5)), Now));
    }

    [Fact]
    public void PastEvent_NoLongerBlocksTheTaskWaitingOnIt()
    {
        var evt = Item(TaskType.Event, start: Now.AddDays(-1), end: Now.AddDays(-1).AddHours(1));
        var waiting = Item(TaskType.Task, deadline: Now.AddDays(1));
        waiting.BlockedByTaskId = evt.Id;

        var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(new[] { evt, waiting }, Now, ScoreTriggerSource.ManualUpdate);

        Assert.False(logs[waiting.Id].IsBlocked);
        Assert.Equal(0.0, logs[evt.Id].FinalScore);
    }

    [Fact]
    public void Item_WithNoDates_HasNoDeadlinePressure()
    {
        Assert.Equal(0.0, Score(Item(TaskType.Task)).DeadlineComponent);
        Assert.Equal(0.0, Score(Item(TaskType.Event)).DeadlineComponent);
    }
}

/// <summary>The new fields are validated and persisted by the create/update commands.</summary>
public class TaskScheduleFieldsTests
{
    private static readonly DateTime Future = DateTime.UtcNow.AddDays(5);

    private static CreateTaskCommand Create(
        TaskType type = TaskType.Task, DateTime? start = null, DateTime? deadline = null, double? effort = null,
        bool recurring = false, RecurrenceFrequency? frequency = null, int? interval = null, DateTime? recurrenceEnds = null,
        DateTime? end = null, int? weekdays = null) =>
        new(Guid.NewGuid(), "T", null, null, deadline, 5, null, null, type, start, effort, recurring, frequency, interval, weekdays, recurrenceEnds, EndUtc: end);

    private static bool IsValid(CreateTaskCommand command) => new CreateTaskCommandValidator().Validate(command).IsValid;

    [Fact]
    public void Validator_PlainTask_IsValid() => Assert.True(IsValid(Create(deadline: Future)));

    [Fact]
    public void Validator_EventWithoutStart_IsRejected() => Assert.False(IsValid(Create(TaskType.Event, deadline: Future)));

    [Fact]
    public void Validator_EventWithStartAndEnd_IsValid() =>
        Assert.True(IsValid(Create(TaskType.Event, start: Future, end: Future.AddHours(2))));

    [Fact]
    public void Validator_EndBeforeStart_IsRejected() =>
        Assert.False(IsValid(Create(TaskType.Event, start: Future.AddDays(1), end: Future)));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(9000.0)]
    public void Validator_EffortOutOfRange_IsRejected(double effort) =>
        Assert.False(IsValid(Create(deadline: Future, effort: effort)));

    [Fact]
    public void Validator_RecurringWithoutFrequencyOrInterval_IsRejected() =>
        Assert.False(IsValid(Create(deadline: Future, recurring: true)));

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Validator_RecurringIntervalOutOfRange_IsRejected(int interval) =>
        Assert.False(IsValid(Create(deadline: Future, recurring: true, frequency: RecurrenceFrequency.Daily, interval: interval)));

    [Fact]
    public void Validator_WeeklyWithAnUnknownDay_IsRejected() =>
        Assert.False(IsValid(Create(deadline: Future, recurring: true, frequency: RecurrenceFrequency.Weekly, interval: 1, weekdays: 128)));

    [Fact]
    public void Validator_WeeklyOnMondayAndFriday_IsValid() =>
        Assert.True(IsValid(Create(deadline: Future, recurring: true, frequency: RecurrenceFrequency.Weekly, interval: 1, weekdays: 1 | 16)));

    [Fact]
    public void Validator_RecurringWithoutAnyDate_IsRejected() =>
        Assert.False(IsValid(Create(recurring: true, frequency: RecurrenceFrequency.Daily, interval: 1)));

    [Fact]
    public void Validator_RecurringEndingBeforeFirstOccurrence_IsRejected() =>
        Assert.False(IsValid(Create(deadline: Future, recurring: true, frequency: RecurrenceFrequency.Daily, interval: 1, recurrenceEnds: Future.AddDays(-1))));

    [Fact]
    public void Validator_RecurringWithIntervalAndEnd_IsValid() =>
        Assert.True(IsValid(Create(deadline: Future, recurring: true, frequency: RecurrenceFrequency.Weekly, interval: 1, recurrenceEnds: Future.AddDays(60))));

    [Fact]
    public async Task CreateThenUpdate_PersistsAndReturnsTheNewFields()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        var cache = new Mock<ICacheService>().Object;
        var notifications = new PlanFlow.Application.Notifications.NotificationService(db);

        var created = await new CreateTaskCommandHandler(db, cache, notifications).Handle(
            new CreateTaskCommand(team.Id, "Standup", null, null, null, 5, null, owner.Id,
                TaskType.Event, Future, 0.5, true, RecurrenceFrequency.Daily, 1, null, Future.AddDays(30), EndUtc: Future.AddHours(1)),
            CancellationToken.None);

        Assert.Equal(TaskType.Event, created.TaskType);
        Assert.Equal(Future, created.StartUtc);
        Assert.Equal(Future.AddHours(1), created.EndUtc);
        Assert.Null(created.DeadlineUtc);
        Assert.Equal(0.5, created.EstimatedEffortHours);
        Assert.True(created.IsRecurring);
        Assert.Equal(RecurrenceFrequency.Daily, created.RecurrenceFrequency);
        Assert.Equal(1, created.RecurrenceInterval);

        // Switching recurrence off for the whole series clears the rule so stale values never linger.
        var updated = await new UpdateTaskCommandHandler(db, cache, notifications).Handle(
            new UpdateTaskCommand(created.Id, "Standup", null, TaskStatus.Backlog, null, Future.AddHours(1), 5, null, null, null, owner.Id,
                TaskType.Task, null, 2.0, false, RecurrenceFrequency.Daily, 1, null, Future.AddDays(30), Scope: SeriesScope.All),
            CancellationToken.None);

        Assert.Equal(TaskType.Task, updated.TaskType);
        Assert.Equal(2.0, updated.EstimatedEffortHours);
        Assert.False(updated.IsRecurring);
        Assert.Null(updated.RecurrenceFrequency);
        Assert.Null(updated.RecurrenceInterval);
        Assert.Null(updated.RecurrenceEndsAtUtc);
    }

    [Fact]
    public void Validator_NewEventThatIsAlreadyOver_IsRejected() =>
        Assert.False(IsValid(Create(TaskType.Event, start: DateTime.UtcNow.AddHours(-3), end: DateTime.UtcNow.AddHours(-2))));

    [Fact]
    public void Validator_NewEventWithoutEnd_UsesTheDefaultHour() =>
        Assert.True(IsValid(Create(TaskType.Event, start: DateTime.UtcNow.AddMinutes(-20))));

    [Fact]
    public void Validator_EventIgnoresAStrayDeadline_EvenAPastOne() =>
        Assert.True(IsValid(Create(TaskType.Event, start: Future, deadline: DateTime.UtcNow.AddDays(-3))));

    [Fact]
    public void Validator_RecurringEvent_RepeatsFromItsStart() =>
        Assert.True(IsValid(Create(TaskType.Event, start: Future, recurring: true, frequency: RecurrenceFrequency.Weekly, interval: 1)));

    [Fact]
    public void Validator_RecurringEventEndingBeforeItsStart_IsRejected() =>
        Assert.False(IsValid(Create(TaskType.Event, start: Future, recurring: true, frequency: RecurrenceFrequency.Weekly, interval: 1, recurrenceEnds: Future.AddDays(-1))));

    [Fact]
    public void Validator_TaskStartAfterItsDeadline_IsRejected() =>
        Assert.False(IsValid(Create(TaskType.Task, start: Future.AddDays(1), deadline: Future)));

    [Fact]
    public void Validator_TaskStartBeforeOrAtItsDeadline_IsAccepted()
    {
        Assert.True(IsValid(Create(TaskType.Task, start: Future.AddDays(-1), deadline: Future)));
        Assert.True(IsValid(Create(TaskType.Task, start: Future, deadline: Future)));
        Assert.True(IsValid(Create(TaskType.Task, start: null, deadline: Future)));
    }

    [Fact]
    public async Task ChangingAnEventToATask_DropsItsStartAndEnd()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        var cache = new Mock<ICacheService>().Object;
        var notifications = new PlanFlow.Application.Notifications.NotificationService(db);

        var created = await new CreateTaskCommandHandler(db, cache, notifications).Handle(
            new CreateTaskCommand(team.Id, "Meeting", null, null, null, 5, null, owner.Id,
                TaskType.Event, Future, null, false, null, null, EndUtc: Future.AddHours(1)),
            CancellationToken.None);
        Assert.Equal(Future, created.StartUtc);

        // The caller still sends the old start along: a Task must not keep it.
        var updated = await new UpdateTaskCommandHandler(db, cache, notifications).Handle(
            new UpdateTaskCommand(created.Id, "Meeting", null, TaskStatus.Backlog, null, Future.AddHours(1), 5, null, null, null, owner.Id,
                TaskType.Task, Future, null, false, null, null, EndUtc: Future.AddHours(1)),
            CancellationToken.None);

        Assert.Equal(TaskType.Task, updated.TaskType);
        Assert.Null(updated.StartUtc);
        Assert.Null(updated.EndUtc);
        Assert.Equal(Future.AddHours(1), updated.DeadlineUtc);
    }

    [Fact]
    public async Task CreateTask_WithoutNewFields_DefaultsToAPlainTask()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));

        var created = await new CreateTaskCommandHandler(db, new Mock<ICacheService>().Object, new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new CreateTaskCommand(team.Id, "Old client", null, null, null, 5, null, owner.Id), CancellationToken.None);

        Assert.Equal(TaskType.Task, created.TaskType);
        Assert.Null(created.EstimatedEffortHours);
        Assert.False(created.IsRecurring);
        Assert.False(created.IsDataIncomplete);
    }
}
