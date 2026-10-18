using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Domain.Services;

public class TaskStatusRulesTests
{
    [Theory]
    [InlineData(TaskStatus.Backlog)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Cancelled)]
    public void Task_AcceptsTaskStatusesAndCancelled(TaskStatus status) =>
        Assert.True(TaskStatusRules.IsValidFor(TaskType.Task, status));

    [Theory]
    [InlineData(TaskStatus.Confirmed)]
    [InlineData(TaskStatus.Tentative)]
    public void Task_RejectsEventStatuses(TaskStatus status) =>
        Assert.False(TaskStatusRules.IsValidFor(TaskType.Task, status));

    [Theory]
    [InlineData(TaskStatus.Confirmed)]
    [InlineData(TaskStatus.Tentative)]
    [InlineData(TaskStatus.Cancelled)]
    public void Event_AcceptsEventStatusesAndCancelled(TaskStatus status) =>
        Assert.True(TaskStatusRules.IsValidFor(TaskType.Event, status));

    [Theory]
    [InlineData(TaskStatus.Backlog)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.Completed)]
    public void Event_RejectsTaskStatuses(TaskStatus status) =>
        Assert.False(TaskStatusRules.IsValidFor(TaskType.Event, status));

    [Fact]
    public void DefaultFor_TaskIsBacklog_EventIsConfirmed()
    {
        Assert.Equal(TaskStatus.Backlog, TaskStatusRules.DefaultFor(TaskType.Task));
        Assert.Equal(TaskStatus.Confirmed, TaskStatusRules.DefaultFor(TaskType.Event));
    }

    [Theory]
    [InlineData(TaskType.Task, TaskStatus.InProgress, TaskStatus.InProgress)]   // already legal: kept
    [InlineData(TaskType.Task, TaskStatus.Confirmed, TaskStatus.Backlog)]       // event -> task
    [InlineData(TaskType.Event, TaskStatus.Completed, TaskStatus.Confirmed)]    // task -> event
    [InlineData(TaskType.Event, TaskStatus.Cancelled, TaskStatus.Cancelled)]    // Cancelled is common to both
    [InlineData(TaskType.Event, TaskStatus.Tentative, TaskStatus.Tentative)]
    public void Normalize_KeepsLegalStatusOtherwiseFallsBackToTheTypeDefault(TaskType type, TaskStatus input, TaskStatus expected) =>
        Assert.Equal(expected, TaskStatusRules.Normalize(type, input));

    [Theory]
    [InlineData(TaskStatus.Completed, true)]
    [InlineData(TaskStatus.Cancelled, true)]
    [InlineData(TaskStatus.Backlog, false)]
    [InlineData(TaskStatus.InProgress, false)]
    [InlineData(TaskStatus.Confirmed, false)]
    [InlineData(TaskStatus.Tentative, false)]
    public void IsFinished_OnlyCompletedAndCancelled(TaskStatus status, bool expected) =>
        Assert.Equal(expected, TaskStatusRules.IsFinished(status));

    [Fact]
    public void StoredNumbers_KeepTheirOldMeaning_SoNoDataNeedsRewritingForThem()
    {
        // 0 was Todo, 1 InProgress, 3 Done, 4 Cancelled; 2 (Blocked) is retired and migrated to 0.
        Assert.Equal(0, (int)TaskStatus.Backlog);
        Assert.Equal(1, (int)TaskStatus.InProgress);
        Assert.Equal(3, (int)TaskStatus.Completed);
        Assert.Equal(4, (int)TaskStatus.Cancelled);
        Assert.False(Enum.IsDefined((TaskStatus)2));
    }
}
