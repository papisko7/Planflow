using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Common;

/// <summary>
/// Algorithm version 2: informational items stay out of the ranking, an unverified item gets no "imminent"
/// floor, a task's planned start feeds the Scheduled component, and all-day items are read in the assignee's time zone.
/// </summary>
public class ScoringProfileAndScheduledTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");     // UTC+2 in October
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); // UTC-4 in October

    private static CalendarItem Item(
        TaskType type = TaskType.Task, EventTemplate template = EventTemplate.Default, TaskStatus? status = null,
        DateTime? start = null, DateTime? end = null, DateTime? deadline = null, int? impact = 5, double? effort = null,
        bool incomplete = false, bool allDay = false, Guid? blockedBy = null) => new()
    {
        Id = Guid.NewGuid(),
        TeamId = Guid.NewGuid(),
        Title = "T",
        TaskType = type,
        EventTemplate = template,
        Status = status ?? TaskStatusRules.DefaultFor(type),
        StartUtc = start,
        EndUtc = end,
        DeadlineUtc = deadline,
        ImpactScore = impact,
        EstimatedEffortHours = effort,
        IsDataIncomplete = incomplete,
        IsAllDay = allDay,
        BlockedByTaskId = blockedBy
    };

    private static UrgencyScoreLog Score(CalendarItem item, TimeZoneInfo? zone = null) =>
        CalendarItemUrgencyScoreFactory.BuildScoreLog(item, 0.0, false, Now, zone: zone);

    // ---- Scoring profiles: informational items are not ranked ----

    [Theory]
    [InlineData(TaskType.Task, EventTemplate.Default, true)]
    [InlineData(TaskType.Event, EventTemplate.Default, true)]
    [InlineData(TaskType.Event, EventTemplate.OutOfOffice, false)]
    [InlineData(TaskType.Event, EventTemplate.FocusTime, false)]
    [InlineData(TaskType.Event, EventTemplate.WorkingLocation, false)]
    [InlineData(TaskType.Event, EventTemplate.Birthday, false)]
    public void IsRanked_FollowsTheTemplatesScoringProfile(TaskType type, EventTemplate template, bool expected) =>
        Assert.Equal(expected, CalendarItemUrgencyScoreFactory.IsRanked(Item(type, template)));

    [Theory]
    [InlineData(EventTemplate.OutOfOffice)]
    [InlineData(EventTemplate.FocusTime)]
    [InlineData(EventTemplate.WorkingLocation)]
    [InlineData(EventTemplate.Birthday)]
    public void InformationalItem_GetsNoLogInTheTeamPass_AndAZeroScoreIfAskedDirectly(EventTemplate template)
    {
        // Even an informational event that starts in ten minutes is not a decision.
        var item = Item(TaskType.Event, template, start: Now.AddMinutes(10), end: Now.AddHours(1), impact: 10);

        var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(new[] { item }, Now, ScoreTriggerSource.ManualUpdate);
        var direct = Score(item);

        Assert.Empty(logs);
        Assert.Equal(0.0, direct.FinalScore);
        Assert.Equal("Informational item - shown in the calendar, not ranked", direct.ReasonText);
    }

    [Fact]
    public void InformationalItem_IsOutsideTheBlockingChain()
    {
        var leave = Item(TaskType.Event, EventTemplate.OutOfOffice, start: Now.AddDays(1), end: Now.AddDays(2));
        var task = Item(deadline: Now.AddHours(2), blockedBy: leave.Id);

        var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(new[] { leave, task }, Now, ScoreTriggerSource.ManualUpdate);

        Assert.False(logs.ContainsKey(leave.Id));
        Assert.False(logs[task.Id].IsBlocked); // an absence does not "hold up" the task in the score
    }

    [Fact]
    public void InformationalItem_DoesNotInheritUrgencyFromWhatItBlocks()
    {
        var urgent = Item(deadline: Now.AddHours(2));
        var focus = Item(TaskType.Event, EventTemplate.FocusTime, start: Now.AddDays(3), end: Now.AddDays(3).AddHours(2), blockedBy: null);
        urgent.BlockedByTaskId = focus.Id;

        var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(new[] { urgent, focus }, Now, ScoreTriggerSource.ManualUpdate);

        Assert.Equal(0.0, logs[urgent.Id].BlockingComponent);
        Assert.False(logs[urgent.Id].IsBlocked);
    }

    [Fact]
    public void RankedItems_EveryLogCarriesTheCurrentAlgorithmVersion()
    {
        var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(
            new[] { Item(deadline: Now.AddDays(2)), Item(TaskType.Event, start: Now.AddHours(5), end: Now.AddHours(6)) },
            Now, ScoreTriggerSource.ScheduledRecalculation);

        Assert.All(logs.Values, l => Assert.Equal(UrgencyScoreCalculator.AlgorithmVersion, l.AlgorithmVersion));
        Assert.Equal(2, UrgencyScoreCalculator.AlgorithmVersion);
    }

    // ---- Unverified items: no floor ----

    [Fact]
    public void UnverifiedOverdueTask_GetsNoFloor_SoItIsNotCritical()
    {
        var unverified = Score(Item(deadline: Now.AddDays(-3), impact: 5, incomplete: true));
        var verified = Score(Item(deadline: Now.AddDays(-3), impact: 5));

        // Deadline 1.0, impact 0.5, nothing else: (0.40 + 0.25 * 0.5) / 0.95.
        Assert.Equal((0.40 + 0.125) / 0.95, unverified.FinalScore, precision: 9);
        Assert.StartsWith("[Medium]", unverified.ReasonText);
        Assert.True(verified.FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
        Assert.StartsWith("[Critical]", verified.ReasonText);
    }

    [Fact]
    public void UnverifiedEventStartingSoon_GetsNoFloor()
    {
        var unverified = Score(Item(TaskType.Event, start: Now.AddMinutes(30), end: Now.AddHours(1), impact: 5, incomplete: true));
        var verified = Score(Item(TaskType.Event, start: Now.AddMinutes(30), end: Now.AddHours(1), impact: 5));

        Assert.True(unverified.FinalScore < UrgencyScoreCalculator.ImminentDeadlineFloor);
        Assert.True(verified.FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
    }

    [Fact]
    public void UnverifiedItem_StillInheritsFromWhatItBlocks_AndSaysItsDetailsAreNotConfirmed()
    {
        var blocker = Item(deadline: Now.AddDays(20), incomplete: true);
        var urgent = Item(deadline: Now.AddHours(2), blockedBy: blocker.Id);

        var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(new[] { blocker, urgent }, Now, ScoreTriggerSource.ManualUpdate);

        Assert.True(logs[blocker.Id].BlockingComponent > 0.5);
        Assert.Contains("details not confirmed yet", logs[blocker.Id].ReasonText);
    }

    // ---- Scheduled: the planned start of a task ----

    [Fact]
    public void PlannedStartInTheFuture_FollowsTheSevenDayCurve()
    {
        var log = Score(Item(deadline: Now.AddDays(30), start: Now.AddDays(7)));

        Assert.Equal(Math.Exp(-1), log.ScheduledComponent!.Value, precision: 9);
        Assert.Contains("planned start in 7d", log.ReasonText);
    }

    [Fact]
    public void PlannedStartThatHasPassed_ForATaskStillInBacklog_IsOne()
    {
        var log = Score(Item(deadline: Now.AddDays(30), start: Now.AddDays(-2)));

        Assert.Equal(1.0, log.ScheduledComponent);
        Assert.Contains("not started, planned start was 2d ago", log.ReasonText);
    }

    [Fact]
    public void PlannedStart_IsLeftOutOnceTheTaskIsInProgress()
    {
        var log = Score(Item(deadline: Now.AddDays(30), start: Now.AddDays(-2), status: TaskStatus.InProgress));

        Assert.Null(log.ScheduledComponent);
        Assert.DoesNotContain("planned start", log.ReasonText);
    }

    [Fact]
    public void PlannedStart_IsLeftOutForAnUnverifiedTask_AndForAnEvent_AndWhenThereIsNone()
    {
        Assert.Null(Score(Item(deadline: Now.AddDays(30), start: Now.AddDays(-2), incomplete: true)).ScheduledComponent);
        Assert.Null(Score(Item(TaskType.Event, start: Now.AddDays(1), end: Now.AddDays(1).AddHours(1))).ScheduledComponent);
        Assert.Null(Score(Item(deadline: Now.AddDays(30))).ScheduledComponent);
    }

    [Fact]
    public void PlannedStart_AddsToTheScore_ExactlyByItsWeight()
    {
        var without = Score(Item(deadline: Now.AddDays(10), impact: 5));
        var due = Score(Item(deadline: Now.AddDays(10), impact: 5, start: Now.AddDays(-1)));

        // With the start counted all five weights add up to 1; without it the other four are scaled up by 1 / 0.95.
        var deadline = Math.Exp(-10.0 / 7.0);
        Assert.Equal(0.40 * deadline + 0.25 * 0.5 + 0.05 * 1.0, due.FinalScore, precision: 9);
        Assert.Equal((0.40 * deadline + 0.25 * 0.5) / 0.95, without.FinalScore, precision: 9);
    }

    // ---- All-day items are read in the assignee's time zone ----

    [Fact]
    public void AllDayTaskDeadline_RunsOutAtTheEndOfThatDayInTheUsersZone()
    {
        // Due on 17 Oct, stored as the literal day. In Warsaw the day ends at 18 Oct 00:00 local = 22:00 UTC; in UTC at 00:00 UTC.
        var day = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var item = Item(deadline: day, allDay: true, impact: 5);

        Assert.Contains("due in 10h", Score(item, Warsaw).ReasonText);
        Assert.Contains("due in 12h", Score(item).ReasonText);
    }

    [Fact]
    public void AllDayTaskDueToday_IsNotOverdueUntilTheDayIsOver()
    {
        var day = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var item = Item(deadline: day, allDay: true);

        Assert.DoesNotContain("overdue", Score(item, Warsaw).ReasonText);
        var nextMorning = CalendarItemUrgencyScoreFactory.BuildScoreLog(item, 0.0, false, day.AddDays(1).AddHours(5), zone: Warsaw);
        Assert.Contains("overdue by 7h", nextMorning.ReasonText); // 18 Oct 05:00 UTC = 07:00 in Warsaw, 7h after the day ended
    }

    [Fact]
    public void AllDayEvent_IsOverWhenItsLastDayEndsForTheUser_NotAtMidnightUtc()
    {
        // A one-day event on 16 Oct: stored 16 Oct 00:00 to 17 Oct 00:00 UTC. At 17 Oct 02:00 UTC it is already over in UTC,
        // but in New York (UTC-4) the 16th lasts until 17 Oct 04:00 UTC.
        var item = Item(TaskType.Event, start: new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc), allDay: true);
        var twoAmUtc = new DateTime(2026, 10, 17, 2, 0, 0, DateTimeKind.Utc);

        Assert.True(CalendarItemUrgencyScoreFactory.IsPastEvent(item, twoAmUtc));
        Assert.False(CalendarItemUrgencyScoreFactory.IsPastEvent(item, twoAmUtc, NewYork));
        Assert.True(CalendarItemUrgencyScoreFactory.IsPastEvent(item, twoAmUtc.AddHours(3), NewYork));
    }

    [Fact]
    public void AllDayTaskStart_TodayIsNotLate_ButAfterTheDayItIs()
    {
        var startDay = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var item = Item(deadline: startDay.AddDays(10), start: startDay, allDay: true);

        var today = Score(item, Warsaw); // 17 Oct 12:00 UTC is 14:00 in Warsaw, the middle of the start day
        Assert.Equal(1.0, today.ScheduledComponent);
        Assert.Contains("planned start today", today.ReasonText);

        var twoDaysLater = CalendarItemUrgencyScoreFactory.BuildScoreLog(item, 0.0, false, Now.AddDays(2), zone: Warsaw);
        Assert.Contains("not started, planned start was", twoDaysLater.ReasonText);
    }

    [Fact]
    public void AllDayTime_StartOfLocalDay_UsesTheZonesOffset()
    {
        var day = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 10, 16, 22, 0, 0, DateTimeKind.Utc), AllDayTime.StartOfLocalDayUtc(day, Warsaw));
        Assert.Equal(new DateTime(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc), AllDayTime.StartOfLocalDayUtc(day, NewYork));
        Assert.Equal(day, AllDayTime.StartOfLocalDayUtc(day, TimeZoneInfo.Utc));
    }

    [Fact]
    public void AllDayTime_ADayWhoseMidnightDoesNotExist_StartsAtTheFirstMomentThatDoes()
    {
        // Sao Paulo moved its clocks from 00:00 straight to 01:00 on 4 Nov 2018, so that midnight never happened.
        var saoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var start = AllDayTime.StartOfLocalDayUtc(new DateTime(2018, 11, 4, 0, 0, 0, DateTimeKind.Utc), saoPaulo);

        Assert.Equal(new DateTime(2018, 11, 4, 3, 0, 0, DateTimeKind.Utc), start); // 01:00 at UTC-2
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public void AllDayTime_UnknownZone_FallsBackToUtc(string? id) => Assert.Equal(TimeZoneInfo.Utc, AllDayTime.ZoneOrUtc(id));
}
