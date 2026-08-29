using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Tests.Application.Tasks.Common;

/// <summary>
/// Unit tests for <see cref="TaskUrgencyScoreFactory"/>.
/// Verifies normalization of TaskItem fields to [0,1] range and the resulting score log.
/// </summary>
public class TaskUrgencyScoreFactoryTests
{
    private static readonly DateTime ReferenceTime = new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

    private TaskItem CreateTaskItem(
        Guid? id = null,
        DateTime? deadline = null,
        int impactScore = 5,
        Guid? blockedByTaskId = null,
        double? manualOverride = null,
        DateTime? overrideExpiresAt = null)
    {
        return new TaskItem
        {
            Id = id ?? Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Title = "Test Task",
            DeadlineUtc = deadline,
            ImpactScore = impactScore,
            BlockedByTaskId = blockedByTaskId,
            ManualUrgencyOverride = manualOverride,
            ManualUrgencyOverrideExpiresAtUtc = overrideExpiresAt,
            Status = PlanFlow.Domain.Enums.TaskStatus.Todo
        };
    }

    [Fact]
    public void BuildScoreLog_NoDeadline_DeadlineComponentZero()
    {
        var task = CreateTaskItem(deadline: null);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.0, log.DeadlineComponent);
    }

    [Fact]
    public void BuildScoreLog_OverdueTask_DeadlineComponentOne()
    {
        var now = ReferenceTime;
        var overdueDeadline = now.AddDays(-1);
        var task = CreateTaskItem(deadline: overdueDeadline);

        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, now);

        Assert.Equal(1.0, log.DeadlineComponent);
    }

    [Fact]
    public void BuildScoreLog_DeadlineTomorrow_DeadlineComponentHigh()
    {
        var now = ReferenceTime;
        var tomorrow = now.AddDays(1);
        var task = CreateTaskItem(deadline: tomorrow);

        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, now);

        Assert.True(log.DeadlineComponent > 0.9);
        Assert.Equal(1.0 - 1.0 / 30.0, log.DeadlineComponent, precision: 2);
    }

    [Fact]
    public void BuildScoreLog_DeadlineIn30Days_DeadlineComponentZero()
    {
        var now = ReferenceTime;
        var in30Days = now.AddDays(30);
        var task = CreateTaskItem(deadline: in30Days);

        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, now);

        Assert.Equal(0.0, log.DeadlineComponent, precision: 2);
    }

    [Fact]
    public void BuildScoreLog_DeadlineIn15Days_DeadlineComponentMid()
    {
        var now = ReferenceTime;
        var in15Days = now.AddDays(15);
        var task = CreateTaskItem(deadline: in15Days);

        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, now);

        Assert.True(log.DeadlineComponent > 0.4);
        Assert.True(log.DeadlineComponent < 0.6);
    }

    [Fact]
    public void BuildScoreLog_ImpactZero_ImpactComponentZero()
    {
        var task = CreateTaskItem(impactScore: 0);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.0, log.ImpactComponent);
    }

    [Fact]
    public void BuildScoreLog_ImpactTen_ImpactComponentOne()
    {
        var task = CreateTaskItem(impactScore: 10);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(1.0, log.ImpactComponent);
    }

    [Fact]
    public void BuildScoreLog_ImpactFive_ImpactComponentHalf()
    {
        var task = CreateTaskItem(impactScore: 5);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.5, log.ImpactComponent, precision: 2);
    }

    [Fact]
    public void BuildScoreLog_ImpactAbove10_Clamped()
    {
        var task = CreateTaskItem(impactScore: 15);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(1.0, log.ImpactComponent);
    }

    [Fact]
    public void BuildScoreLog_BlockingZeroTasks_BlockingComponentZero()
    {
        var task = CreateTaskItem();
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.0, log.BlockingComponent);
    }

    [Fact]
    public void BuildScoreLog_BlockingOneTask_BlockingComponentSmall()
    {
        var task = CreateTaskItem();
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 1, ReferenceTime);

        Assert.Equal(0.2, log.BlockingComponent, precision: 2);
    }

    [Fact]
    public void BuildScoreLog_BlockingFiveTasks_BlockingComponentOne()
    {
        var task = CreateTaskItem();
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 5, ReferenceTime);

        Assert.Equal(1.0, log.BlockingComponent);
    }

    [Fact]
    public void BuildScoreLog_BlockingMoreThanFive_BlockingComponentClamped()
    {
        var task = CreateTaskItem();
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 10, ReferenceTime);

        Assert.Equal(1.0, log.BlockingComponent);
    }

    [Fact]
    public void BuildScoreLog_NoManualOverride_OverrideComponentZero()
    {
        var task = CreateTaskItem(manualOverride: null);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.0, log.UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_ActiveManualOverride_OverrideComponentUsed()
    {
        var task = CreateTaskItem(
            manualOverride: 0.7,
            overrideExpiresAt: ReferenceTime.AddHours(1));
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.7, log.UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_ExpiredManualOverride_OverrideComponentZero()
    {
        var now = ReferenceTime;
        var task = CreateTaskItem(
            manualOverride: 0.7,
            overrideExpiresAt: now.AddHours(-1));
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, now);

        Assert.Equal(0.0, log.UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_ManualOverrideExactlyExpires_OverrideComponentZero()
    {
        var now = ReferenceTime;
        var task = CreateTaskItem(
            manualOverride: 0.7,
            overrideExpiresAt: now);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, now);

        Assert.Equal(0.0, log.UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_ManualOverrideOutOfRange_Clamped()
    {
        var task = CreateTaskItem(
            manualOverride: 1.5,
            overrideExpiresAt: ReferenceTime.AddHours(1));
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(1.0, log.UserOverrideComponent);
    }

    [Fact]
    public void BuildScoreLog_AiComponentAlwaysZero_UntilPhase22()
    {
        var task = CreateTaskItem();
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.0, log.AiComponent);
        Assert.True(log.AiFallbackUsed);
    }

    [Fact]
    public void BuildScoreLog_FinalScoreCalculated()
    {
        var task = CreateTaskItem(
            deadline: ReferenceTime.AddDays(1),
            impactScore: 10,
            manualOverride: 0.5,
            overrideExpiresAt: ReferenceTime.AddHours(1));
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 3, ReferenceTime);

        var expectedScore =
            0.35 * log.DeadlineComponent +
            0.20 * log.AiComponent +
            0.15 * log.BlockingComponent +
            0.20 * log.ImpactComponent +
            0.10 * log.UserOverrideComponent;

        Assert.Equal(expectedScore, log.FinalScore, precision: 5);
        Assert.True(log.FinalScore > 0.5);
        Assert.True(log.FinalScore <= 1.0);
    }

    [Fact]
    public void BuildScoreLog_AllComponentsMin_ScoreLow()
    {
        var task = CreateTaskItem(
            deadline: null,
            impactScore: 0,
            manualOverride: null,
            overrideExpiresAt: null);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.0, log.FinalScore);
    }

    [Fact]
    public void BuildScoreLog_AllComponentsMax_ScoreHigh()
    {
        var task = CreateTaskItem(
            deadline: ReferenceTime.AddHours(-1),
            impactScore: 10,
            manualOverride: 1.0,
            overrideExpiresAt: ReferenceTime.AddHours(1));
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 5, ReferenceTime);

        var expectedScore =
            0.35 * 1.0 + 0.20 * 0.0 + 0.15 * 1.0 + 0.20 * 1.0 + 0.10 * 1.0;
        Assert.Equal(expectedScore, log.FinalScore, precision: 5);
    }

    [Fact]
    public void BuildScoreLog_HighDeadlineLowImpact_DeadlineWins()
    {
        var task = CreateTaskItem(
            deadline: ReferenceTime.AddHours(-1),
            impactScore: 0,
            manualOverride: null);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.35, log.FinalScore, precision: 2);
    }

    [Fact]
    public void BuildScoreLog_TaskItemIdPreserved()
    {
        var taskId = Guid.NewGuid();
        var task = CreateTaskItem(id: taskId);
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(taskId, log.TaskItemId);
    }

    [Fact]
    public void BuildScoreLog_IsDeterministic()
    {
        var task = CreateTaskItem(
            deadline: ReferenceTime.AddDays(5),
            impactScore: 7,
            manualOverride: 0.3,
            overrideExpiresAt: ReferenceTime.AddDays(1));

        var log1 = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 2, ReferenceTime);
        var log2 = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 2, ReferenceTime);

        Assert.Equal(log1.FinalScore, log2.FinalScore);
        Assert.Equal(log1.DeadlineComponent, log2.DeadlineComponent);
        Assert.Equal(log1.BlockingComponent, log2.BlockingComponent);
    }

    [Fact]
    public void BuildScoreLog_BlockedTaskDoesNotInfluenceOwnScore()
    {
        var task = CreateTaskItem(blockedByTaskId: Guid.NewGuid());
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedTaskCount: 0, ReferenceTime);

        Assert.Equal(0.0, log.BlockingComponent);
    }

    [Theory]
    [InlineData(1, 0.2)]
    [InlineData(2, 0.4)]
    [InlineData(3, 0.6)]
    [InlineData(4, 0.8)]
    [InlineData(5, 1.0)]
    public void BuildScoreLog_BlockingComponent_ScalesCorrectly(int blockedCount, double expectedComponent)
    {
        var task = CreateTaskItem();
        var log = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedCount, ReferenceTime);

        Assert.Equal(expectedComponent, log.BlockingComponent, precision: 2);
    }
}
