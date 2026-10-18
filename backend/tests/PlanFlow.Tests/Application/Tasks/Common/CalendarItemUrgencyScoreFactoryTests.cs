using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Common;

/// <summary>
/// Unit tests for <see cref="CalendarItemUrgencyScoreFactory"/>.
/// Verifies normalization of CalendarItem fields to [0,1], the deadline curve, and how the dependency chain
/// (blockers inherit urgency, blocked tasks are lowered) shapes the final scores of a team.
/// </summary>
public class CalendarItemUrgencyScoreFactoryTests
{
    private static readonly DateTime ReferenceTime = new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

    private static CalendarItem CreateTaskItem(
        Guid? id = null,
        DateTime? deadline = null,
        int? impactScore = 5,
        Guid? blockedByTaskId = null,
        double? manualOverride = null,
        DateTime? overrideExpiresAt = null,
        TaskStatus status = TaskStatus.Backlog)
    {
        return new CalendarItem
        {
            Id = id ?? Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Title = "Test Task",
            DeadlineUtc = deadline,
            ImpactScore = impactScore,
            BlockedByTaskId = blockedByTaskId,
            ManualUrgencyOverride = manualOverride,
            ManualUrgencyOverrideExpiresAtUtc = overrideExpiresAt,
            Status = status
        };
    }

    private static UrgencyScoreLog Build(CalendarItem task, DateTime? now = null, double blocking = 0.0, bool isBlocked = false) =>
        CalendarItemUrgencyScoreFactory.BuildScoreLog(task, blocking, isBlocked, now ?? ReferenceTime);

    // ---- Deadline curve ----

    [Fact]
    public void BuildScoreLog_NoDeadline_DeadlineComponentZero()
    {
        Assert.Equal(0.0, Build(CreateTaskItem(deadline: null)).DeadlineComponent);
    }

    [Fact]
    public void BuildScoreLog_OverdueTask_DeadlineComponentOne()
    {
        var log = Build(CreateTaskItem(deadline: ReferenceTime.AddDays(-1)));

        Assert.Equal(1.0, log.DeadlineComponent);
    }

    [Theory]
    [InlineData(1, 0.8669)]
    [InlineData(3, 0.6514)]
    [InlineData(7, 0.3679)]
    [InlineData(14, 0.1353)]
    public void BuildScoreLog_DeadlineInNDays_FollowsExponentialCurve(double days, double expected)
    {
        var log = Build(CreateTaskItem(deadline: ReferenceTime.AddDays(days)));

        Assert.Equal(expected, log.DeadlineComponent, precision: 3);
    }

    [Fact]
    public void BuildScoreLog_DeadlineIn30Days_DeadlineComponentNearZero()
    {
        var log = Build(CreateTaskItem(deadline: ReferenceTime.AddDays(30)));

        Assert.True(log.DeadlineComponent < 0.02);
    }

    [Fact]
    public void BuildScoreLog_DeadlineCurve_RisesFastestInTheLastDays()
    {
        double Component(double days) => Build(CreateTaskItem(deadline: ReferenceTime.AddDays(days))).DeadlineComponent;

        // The same one-day step matters far more near the deadline than a month out.
        Assert.True(Component(1) - Component(2) > Component(20) - Component(21));
        Assert.True(Component(0.25) > Component(1) && Component(1) > Component(3));
    }

    // ---- Imminent floor ----

    [Fact]
    public void BuildScoreLog_DueToday_LowImpact_StillAtLeastTheFloor()
    {
        var log = Build(CreateTaskItem(deadline: ReferenceTime.AddHours(5), impactScore: 0));

        Assert.True(log.FinalScore >= UrgencyScoreCalculator.ImminentDeadlineFloor);
    }

    [Fact]
    public void BuildScoreLog_OverdueTaskWithNoImpact_OutranksUndatedTaskWithMaxEverything()
    {
        var overdue = Build(CreateTaskItem(deadline: ReferenceTime.AddDays(-2), impactScore: 0));
        var undated = Build(CreateTaskItem(deadline: null, impactScore: 10, manualOverride: 1.0), blocking: 0.5);

        Assert.True(overdue.FinalScore > undated.FinalScore);
    }

    [Fact]
    public void BuildScoreLog_DeadlineJustOverADayAway_NoFloor()
    {
        var log = Build(CreateTaskItem(deadline: ReferenceTime.AddHours(30), impactScore: 0));

        Assert.True(log.FinalScore < UrgencyScoreCalculator.ImminentDeadlineFloor);
    }

    // ---- Impact ----

    [Fact]
    public void BuildScoreLog_ImpactZero_ImpactComponentZero()
    {
        Assert.Equal(0.0, Build(CreateTaskItem(impactScore: 0)).ImpactComponent);
    }

    [Fact]
    public void BuildScoreLog_ImpactTen_ImpactComponentOne()
    {
        Assert.Equal(1.0, Build(CreateTaskItem(impactScore: 10)).ImpactComponent);
    }

    [Fact]
    public void BuildScoreLog_ImpactFive_ImpactComponentHalf()
    {
        Assert.Equal(0.5, Build(CreateTaskItem(impactScore: 5)).ImpactComponent);
    }

    [Fact]
    public void BuildScoreLog_ImpactAbove10_Clamped()
    {
        Assert.Equal(1.0, Build(CreateTaskItem(impactScore: 15)).ImpactComponent);
    }

    [Fact]
    public void BuildScoreLog_ImpactNotRated_ComponentIsNullNotZero()
    {
        Assert.Null(Build(CreateTaskItem(impactScore: null)).ImpactComponent);
    }

    [Fact]
    public void BuildScoreLog_ImpactNotRated_IsNotPunishedComparedToImpactZero()
    {
        var deadline = ReferenceTime.AddDays(5);
        var unrated = Build(CreateTaskItem(deadline: deadline, impactScore: null));
        var ratedZero = Build(CreateTaskItem(deadline: deadline, impactScore: 0));

        Assert.True(unrated.FinalScore > ratedZero.FinalScore);
    }

    // ---- Override ----

    [Fact]
    public void BuildScoreLog_NoManualOverride_OverrideComponentZero()
    {
        Assert.Equal(0.0, Build(CreateTaskItem(manualOverride: null)).UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_ActiveManualOverride_OverrideComponentUsed()
    {
        var task = CreateTaskItem(manualOverride: 0.7, overrideExpiresAt: ReferenceTime.AddHours(1));

        Assert.Equal(0.7, Build(task).UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_ExpiredManualOverride_OverrideComponentZero()
    {
        var task = CreateTaskItem(manualOverride: 0.7, overrideExpiresAt: ReferenceTime.AddHours(-1));

        Assert.Equal(0.0, Build(task).UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_ManualOverrideExactlyExpires_OverrideComponentZero()
    {
        var task = CreateTaskItem(manualOverride: 0.7, overrideExpiresAt: ReferenceTime);

        Assert.Equal(0.0, Build(task).UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_ManualOverrideOutOfRange_Clamped()
    {
        Assert.Equal(1.0, Build(CreateTaskItem(manualOverride: 3.0)).UserOverrideComponent);
    }

    // ---- Final score / log ----

    [Fact]
    public void BuildScoreLog_FinalScoreMatchesWeightedFormula()
    {
        // Deadline in 7 days (0.3679), impact 6, override 0.5, blocking 0.4 -> plain weighted sum, no floors hit.
        var task = CreateTaskItem(deadline: ReferenceTime.AddDays(7), impactScore: 6, manualOverride: 0.5);

        var log = Build(task, blocking: 0.4);

        var expected = 0.45 * Math.Exp(-1) + 0.20 * 0.4 + 0.25 * 0.6 + 0.10 * 0.5;
        Assert.Equal(expected, log.FinalScore, precision: 5);
    }

    [Fact]
    public void BuildScoreLog_AllComponentsMin_ScoreZero()
    {
        var log = Build(CreateTaskItem(deadline: null, impactScore: 0));

        Assert.Equal(0.0, log.FinalScore);
    }

    [Fact]
    public void BuildScoreLog_AllComponentsMax_ScoreOne()
    {
        var task = CreateTaskItem(deadline: ReferenceTime.AddDays(-1), impactScore: 10, manualOverride: 1.0);

        Assert.Equal(1.0, Build(task, blocking: 1.0).FinalScore, precision: 5);
    }

    [Fact]
    public void BuildScoreLog_TaskItemIdPreserved()
    {
        var task = CreateTaskItem();

        Assert.Equal(task.Id, Build(task).CalendarItemId);
    }

    [Fact]
    public void BuildScoreLog_IsDeterministic()
    {
        var task = CreateTaskItem(deadline: ReferenceTime.AddDays(4), impactScore: 7, manualOverride: 0.3);

        Assert.Equal(Build(task, blocking: 0.2).FinalScore, Build(task, blocking: 0.2).FinalScore);
    }

    [Fact]
    public void BuildScoreLog_BlockedTask_ScoreIsHalvedAndFlagged()
    {
        var task = CreateTaskItem(deadline: ReferenceTime.AddDays(2), impactScore: 8);

        var free = Build(task);
        var blocked = Build(task, isBlocked: true);

        Assert.False(free.IsBlocked);
        Assert.True(blocked.IsBlocked);
        Assert.Equal(free.FinalScore * UrgencyScoreCalculator.BlockedTaskFactor, blocked.FinalScore, precision: 5);
    }

    // ---- Team-wide dependency resolution ----

    private static IReadOnlyDictionary<Guid, UrgencyScoreLog> ScoreTeam(params CalendarItem[] tasks) =>
        CalendarItemUrgencyScoreFactory.BuildTeamScoreLogs(tasks, ReferenceTime, ScoreTriggerSource.ScheduledRecalculation);

    [Fact]
    public void BuildTeamScoreLogs_TaskWithoutDependencies_MatchesSingleScore()
    {
        var task = CreateTaskItem(deadline: ReferenceTime.AddDays(3), impactScore: 4);

        var logs = ScoreTeam(task);

        Assert.Equal(Build(task).FinalScore, logs[task.Id].FinalScore, precision: 9);
        Assert.False(logs[task.Id].IsBlocked);
        Assert.Equal(0.0, logs[task.Id].BlockingComponent);
    }

    [Fact]
    public void BuildTeamScoreLogs_Blocker_InheritsUrgencyOfBlockedTask()
    {
        var blocker = CreateTaskItem(deadline: null, impactScore: 0);
        var blocked = CreateTaskItem(deadline: ReferenceTime.AddHours(6), impactScore: 10, blockedByTaskId: blocker.Id);

        var logs = ScoreTeam(blocker, blocked);

        var blockedEffective = logs[blocked.Id].FinalScore / UrgencyScoreCalculator.BlockedTaskFactor;
        Assert.Equal(0.9 * blockedEffective, logs[blocker.Id].BlockingComponent, precision: 5);
        Assert.True(logs[blocker.Id].FinalScore >= 0.9 * blockedEffective - 1e-9);
        Assert.True(logs[blocker.Id].FinalScore > 0.5, "an idle task blocking an urgent one must rise");
    }

    [Fact]
    public void BuildTeamScoreLogs_BlockedTask_DropsBelowItsBlocker()
    {
        var blocker = CreateTaskItem(deadline: null, impactScore: 0);
        var blocked = CreateTaskItem(deadline: ReferenceTime.AddHours(6), impactScore: 10, blockedByTaskId: blocker.Id);

        var logs = ScoreTeam(blocker, blocked);

        Assert.True(logs[blocked.Id].IsBlocked);
        Assert.True(logs[blocked.Id].FinalScore < logs[blocker.Id].FinalScore,
            "the actionable task (the blocker) must be listed above the one waiting on it");
    }

    [Fact]
    public void BuildTeamScoreLogs_InheritanceFollowsTheWholeChain()
    {
        var a = CreateTaskItem(deadline: null, impactScore: 0);
        var b = CreateTaskItem(deadline: null, impactScore: 0, blockedByTaskId: a.Id);
        var c = CreateTaskItem(deadline: ReferenceTime.AddHours(3), impactScore: 5, blockedByTaskId: b.Id);

        var logs = ScoreTeam(a, b, c);

        // c is due today (>= 0.85 before the blocked penalty); b inherits 0.9 x that, a inherits 0.9 x b.
        Assert.True(logs[b.Id].BlockingComponent >= 0.9 * 0.85 - 1e-9);
        Assert.True(logs[a.Id].BlockingComponent >= 0.9 * 0.9 * 0.85 - 1e-9);
        Assert.True(logs[a.Id].BlockingComponent < logs[b.Id].BlockingComponent);
    }

    [Fact]
    public void BuildTeamScoreLogs_BlockerWithSeveralBlockedTasks_InheritsFromTheMostUrgent()
    {
        var blocker = CreateTaskItem(deadline: null, impactScore: 0);
        var calm = CreateTaskItem(deadline: ReferenceTime.AddDays(20), impactScore: 1, blockedByTaskId: blocker.Id);
        var urgent = CreateTaskItem(deadline: ReferenceTime.AddHours(2), impactScore: 5, blockedByTaskId: blocker.Id);

        var logs = ScoreTeam(blocker, calm, urgent);

        var urgentEffective = logs[urgent.Id].FinalScore / UrgencyScoreCalculator.BlockedTaskFactor;
        Assert.Equal(0.9 * urgentEffective, logs[blocker.Id].BlockingComponent, precision: 5);
    }

    [Fact]
    public void BuildTeamScoreLogs_BlockedTaskScoreDoesNotShrinkWhatTheBlockerInherits()
    {
        // The inheritance uses the blocked task's score BEFORE its penalty, otherwise the blocker would
        // inherit only half of the urgency it is actually holding up.
        var blocker = CreateTaskItem(deadline: null, impactScore: 0);
        var blocked = CreateTaskItem(deadline: ReferenceTime.AddHours(2), impactScore: 0, blockedByTaskId: blocker.Id);

        var logs = ScoreTeam(blocker, blocked);

        Assert.True(logs[blocker.Id].BlockingComponent >= 0.9 * UrgencyScoreCalculator.ImminentDeadlineFloor - 1e-9);
    }

    [Fact]
    public void BuildTeamScoreLogs_FinishedBlocker_NoLongerBlocksOrInheritsAnything()
    {
        // A finished blocker is not part of the active set, so the dependent task is simply free.
        var finishedBlockerId = Guid.NewGuid();
        var dependent = CreateTaskItem(deadline: ReferenceTime.AddDays(2), impactScore: 7, blockedByTaskId: finishedBlockerId);

        var logs = ScoreTeam(dependent);

        Assert.False(logs[dependent.Id].IsBlocked);
        Assert.Equal(Build(dependent).FinalScore, logs[dependent.Id].FinalScore, precision: 9);
    }

    [Fact]
    public void BuildTeamScoreLogs_CorruptedBlockingCycle_TerminatesWithValidScores()
    {
        var a = CreateTaskItem(deadline: ReferenceTime.AddDays(1), impactScore: 5);
        var b = CreateTaskItem(deadline: ReferenceTime.AddDays(1), impactScore: 5, blockedByTaskId: a.Id);
        a.BlockedByTaskId = b.Id;

        var logs = ScoreTeam(a, b);

        Assert.All(logs.Values, l => Assert.InRange(l.FinalScore, 0.0, 1.0));
    }

    [Fact]
    public void BuildTeamScoreLogs_ReturnsOneLogPerTaskWithTheGivenTrigger()
    {
        var logs = ScoreTeam(CreateTaskItem(), CreateTaskItem(), CreateTaskItem());

        Assert.Equal(3, logs.Count);
        Assert.All(logs.Values, l => Assert.Equal(ScoreTriggerSource.ScheduledRecalculation, l.TriggerSource));
    }
}
