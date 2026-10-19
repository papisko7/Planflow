using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Tests.Application.Tasks.Common;

/// <summary>
/// Scenario tests for the urgency algorithm (overdue task, finished event, effort vs. time left,
/// unrated impact, Task vs. Event) together with the deterministic <c>ReasonText</c> shown next to every score.
/// </summary>
public class UrgencyReasonTextTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    private const double Floor = UrgencyScoreCalculator.ImminentDeadlineFloor;

    private static CalendarItem Item(
        TaskType type = TaskType.Task, DateTime? start = null, DateTime? end = null, DateTime? deadline = null,
        double? effort = null, int? impact = 0, double? aiImpact = null) => new()
    {
        Id = Guid.NewGuid(),
        TeamId = Guid.NewGuid(),
        Title = "T",
        TaskType = type,
        StartUtc = start,
        EndUtc = end,
        DeadlineUtc = deadline,
        EstimatedEffortHours = effort,
        ImpactScore = impact,
        AiImpactScore = aiImpact
    };

    private static UrgencyScoreLog Score(CalendarItem item, double blocking = 0.0, bool isBlocked = false) =>
        CalendarItemUrgencyScoreFactory.BuildScoreLog(item, blocking, isBlocked, Now);

    // ---- Case 1: overdue Task keeps the maximum ----

    [Theory]
    [InlineData(-1)]          // just 1 hour late
    [InlineData(-48)]
    [InlineData(-24 * 30)]
    public void OverdueTask_AlwaysKeepsTheMaximumDeadlineComponentAndTheFloor(double hoursFromNow)
    {
        var log = Score(Item(deadline: Now.AddHours(hoursFromNow)));

        Assert.Equal(1.0, log.DeadlineComponent);
        Assert.True(log.FinalScore >= Floor);
        Assert.StartsWith("[Critical]", log.ReasonText);
        Assert.Contains("overdue by", log.ReasonText);
    }

    [Fact]
    public void OverdueTask_ReasonTextNamesHowLateItIs()
    {
        Assert.Equal("[Critical] overdue by 2d, impact 0/10", Score(Item(deadline: Now.AddDays(-2))).ReasonText);
        Assert.Equal("[Critical] overdue by 5h, impact 0/10", Score(Item(deadline: Now.AddHours(-5))).ReasonText);
    }

    [Fact]
    public void OverdueTask_WithEverythingMaxed_StillScoresExactlyOne()
    {
        var task = Item(deadline: Now.AddDays(-3), impact: 10);
        task.ManualUrgencyOverride = 1.0;

        Assert.Equal(1.0, Score(task, blocking: 1.0).FinalScore, precision: 9);
    }

    // ---- Case 2: finished Event is history ----

    [Fact]
    public void FinishedEvent_ScoresZeroAndIsLabelledAsHistory()
    {
        var log = Score(Item(TaskType.Event, start: Now.AddHours(-3), end: Now.AddHours(-2)));

        Assert.Equal(0.0, log.FinalScore);
        Assert.Equal("[Low] Event ended 2h ago - history, no priority", log.ReasonText);
    }

    [Fact]
    public void FinishedEvent_WithoutEnd_UsesTheAssumedOneHourDuration()
    {
        // Started 90 minutes ago, assumed to last 1h -> ended 30 minutes ago.
        var log = Score(Item(TaskType.Event, start: Now.AddMinutes(-90)));

        Assert.Equal(0.0, log.FinalScore);
        Assert.Equal("[Low] Event ended 30 min ago - history, no priority", log.ReasonText);
    }

    [Fact]
    public void FinishedEvent_IgnoresImpactAndOverride_InScoreAndInText()
    {
        var evt = Item(TaskType.Event, start: Now.AddDays(-3), end: Now.AddDays(-3).AddHours(1), impact: 10);
        evt.ManualUrgencyOverride = 1.0;

        var log = Score(evt, blocking: 1.0);

        Assert.Equal(0.0, log.FinalScore);
        Assert.DoesNotContain("impact", log.ReasonText);
        Assert.DoesNotContain("manual", log.ReasonText);
    }

    // ---- Case 3: effort vs. time left, and the floor ----

    [Fact]
    public void EffortLongerThanTimeLeft_IsMaximallyUrgentAndExplainsWhy()
    {
        var log = Score(Item(deadline: Now.AddHours(8), effort: 10));

        Assert.Equal(1.0, log.DeadlineComponent);
        Assert.True(log.FinalScore >= Floor);
        Assert.Equal("[Critical] not enough time: needs 10h, only 8h left, impact 0/10", log.ReasonText);
    }

    [Fact]
    public void EffortShorterThanTimeLeft_IsAnOrdinaryCountdown()
    {
        var log = Score(Item(deadline: Now.AddDays(3), effort: 24));

        Assert.Contains("due in 3d", log.ReasonText);
        Assert.DoesNotContain("not enough time", log.ReasonText);
        Assert.True(log.FinalScore < Floor);
    }

    [Fact]
    public void Floor_StartsExactlyWhenTheTimeLeftEqualsTheEffort()
    {
        // Floor window = effort (4h). Exactly 4h before the deadline the floor applies; one minute earlier it does not.
        var atWindow = Score(Item(deadline: Now.AddHours(4), effort: 4));
        var beforeWindow = Score(Item(deadline: Now.AddHours(4).AddMinutes(1), effort: 4));

        Assert.Equal(Floor, atWindow.FinalScore, precision: 9);
        Assert.True(beforeWindow.FinalScore < Floor);
    }

    [Fact]
    public void Floor_NeverLowersAScoreThatIsAlreadyHigher()
    {
        var task = Item(deadline: Now.AddHours(2), impact: 10);
        task.ManualUrgencyOverride = 1.0;

        Assert.True(Score(task, blocking: 1.0).FinalScore > Floor);
    }

    [Theory]
    [InlineData(0.0, "[Low]")]
    [InlineData(0.39, "[Low]")]
    [InlineData(0.4, "[Medium]")]
    [InlineData(0.59, "[Medium]")]
    [InlineData(0.6, "[High]")]
    [InlineData(0.79, "[High]")]
    [InlineData(0.8, "[Critical]")]
    [InlineData(1.0, "[Critical]")]
    public void PriorityLabel_FollowsTheFourTiers(double score, string expected)
    {
        Assert.Equal(expected, CalendarItemUrgencyScoreFactory.PriorityLabel(score));
    }

    [Fact]
    public void ReasonText_LabelMatchesTheStoredFinalScore()
    {
        var log = Score(Item(deadline: Now.AddDays(5), impact: 6));

        Assert.StartsWith(CalendarItemUrgencyScoreFactory.PriorityLabel(log.FinalScore), log.ReasonText);
    }

    // ---- Case 4: null impact / AI never silently fills it ----

    [Fact]
    public void NullImpact_IsReportedAsNotRated_AndNotPunished()
    {
        var unrated = Score(Item(deadline: Now.AddDays(5), impact: null));

        // Deadline 0.40 * e^(-5/7), re-normalized because the impact (0.25) and Scheduled (0.05) weights are shared out: / 0.70.
        Assert.Null(unrated.ImpactComponent);
        Assert.Equal(0.40 * Math.Exp(-5.0 / 7.0) / 0.70, unrated.FinalScore, precision: 9);
        Assert.Contains("impact not rated", unrated.ReasonText);
    }

    [Fact]
    public void NullImpact_AnAiSuggestionDoesNotChangeTheScore()
    {
        // The AI value is only a suggestion until the user accepts it into ImpactScore.
        var plain = Score(Item(deadline: Now.AddDays(5), impact: null));
        var withSuggestion = Score(Item(deadline: Now.AddDays(5), impact: null, aiImpact: 9));

        Assert.Equal(plain.FinalScore, withSuggestion.FinalScore, precision: 12);
        Assert.Null(withSuggestion.ImpactComponent);
        Assert.Equal(plain.ReasonText, withSuggestion.ReasonText);
    }

    [Fact]
    public void NullImpact_AcceptedSuggestionBecomesARealRating()
    {
        var accepted = Item(deadline: Now.AddDays(5), impact: null, aiImpact: 9);
        accepted.ImpactScore = 9;

        var log = Score(accepted);

        Assert.Equal(0.9, log.ImpactComponent);
        Assert.Contains("impact 9/10", log.ReasonText);
    }

    // ---- Case 5: Task vs. Event ----

    [Fact]
    public void SameMoment_TaskCountsItsDeadlineMinusEffort_EventIgnoresEffort()
    {
        var task = Score(Item(deadline: Now.AddDays(3), effort: 24));
        var evt = Score(Item(TaskType.Event, start: Now.AddDays(3), effort: 24));

        Assert.Equal(Math.Exp(-2.0 / 7.0), task.DeadlineComponent, precision: 9);
        Assert.Equal(Math.Exp(-3.0 / 7.0), evt.DeadlineComponent, precision: 9);
    }

    [Fact]
    public void OneHourAgo_OverdueTaskStaysUrgent_RunningEventIsInProgress()
    {
        var task = Score(Item(deadline: Now.AddHours(-1)));
        var evt = Score(Item(TaskType.Event, start: Now.AddHours(-1), end: Now.AddHours(1)));

        Assert.True(task.FinalScore >= Floor && evt.FinalScore >= Floor);
        Assert.Contains("overdue by 1h", task.ReasonText);
        Assert.Contains("in progress, started 1h ago", evt.ReasonText);
    }

    [Fact]
    public void OneDayAgo_TaskStaysAtMaximum_EventIsHistory()
    {
        var task = Score(Item(deadline: Now.AddDays(-1)));
        var evt = Score(Item(TaskType.Event, start: Now.AddDays(-1), end: Now.AddDays(-1).AddHours(1)));

        Assert.True(task.FinalScore >= Floor);
        Assert.Equal(0.0, evt.FinalScore);
    }

    [Fact]
    public void Event_UsesStartNotDeadline_Task_UsesDeadlineNotStart()
    {
        var evt = Score(Item(TaskType.Event, deadline: Now.AddDays(1)));
        var task = Score(Item(TaskType.Task, start: Now.AddDays(1)));

        Assert.Equal(0.0, evt.DeadlineComponent);
        Assert.Equal("[Low] no start time, impact 0/10", evt.ReasonText);
        // A task's start never stands in for its deadline: it only feeds the Scheduled part (24h away: e^(-1/7)).
        Assert.Equal(0.0, task.DeadlineComponent);
        Assert.Equal(Math.Exp(-1.0 / 7.0), task.ScheduledComponent!.Value, precision: 9);
        Assert.Equal("[Low] no deadline, planned start in 24h, impact 0/10", task.ReasonText);
    }

    [Fact]
    public void UpcomingEvent_ReasonTextCountsDownToItsStart()
    {
        var log = Score(Item(TaskType.Event, start: Now.AddMinutes(45)));

        Assert.Equal("[Critical] starts in 45 min, impact 0/10", log.ReasonText);
    }

    // ---- ReasonText: determinism and extras ----

    [Fact]
    public void ReasonText_IsDeterministicAndFitsTheColumn()
    {
        var task = Item(deadline: Now.AddHours(30), effort: 3, impact: 7);
        task.ManualUrgencyOverride = 0.5;

        var first = Score(task, blocking: 0.4, isBlocked: true).ReasonText;
        var second = Score(task, blocking: 0.4, isBlocked: true).ReasonText;

        Assert.Equal(first, second);
        Assert.True(first!.Length <= 512);
        // Effective score ~0.69, halved by the blocked penalty -> ~0.34 -> Low.
        Assert.Equal(
            "[Low] due in 30h, holds up urgent work (inherits 40%), blocked by an unfinished task (score halved), impact 7/10, manual boost active",
            first);
    }

    [Fact]
    public void ReasonText_InTeamScoring_MentionsBlockerAndBlockedRoles()
    {
        var blocker = Item(impact: 0);
        var blocked = Item(deadline: Now.AddHours(6), impact: 10);
        blocked.BlockedByTaskId = blocker.Id;

        var logs = CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(new[] { blocker, blocked }, Now, ScoreTriggerSource.ManualUpdate);

        Assert.Contains("holds up urgent work", logs[blocker.Id].ReasonText);
        Assert.Contains("blocked by an unfinished task (score halved)", logs[blocked.Id].ReasonText);
    }
}
