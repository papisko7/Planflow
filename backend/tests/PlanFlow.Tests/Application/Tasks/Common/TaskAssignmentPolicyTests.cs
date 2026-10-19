using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Tasks.Common;

namespace PlanFlow.Tests.Application.Tasks.Common;

/// <summary>
/// Step 7.5.10b: who may change a task's assignee, and the lock an Owner/Admin puts on an assignment. The rules used to sit in the
/// controller; here they are plain functions, so every row of the table below is a test.
/// </summary>
public class TaskAssignmentPolicyTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Boss = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    // ---- Owner / Admin ----

    [Fact]
    public void Manager_GivesAnUnassignedTaskToSomeone_LocksIt()
    {
        var outcome = TaskAssignmentPolicy.Decide(Boss, true, null, null, false, Me);

        Assert.Equal(new AssignmentOutcome(Boss, true), outcome);
    }

    [Fact]
    public void Manager_MovesALockedTaskToAnotherPerson_LocksItAgainAsTheNewAssigner()
    {
        var second = Guid.NewGuid();

        var outcome = TaskAssignmentPolicy.Decide(second, true, Me, Boss, true, Other);

        Assert.Equal(new AssignmentOutcome(second, true), outcome);
    }

    [Fact]
    public void Manager_TakesTheTaskForThemselves_IsNotLocked()
    {
        var outcome = TaskAssignmentPolicy.Decide(Boss, true, null, null, false, Boss);

        Assert.Equal(new AssignmentOutcome(null, false), outcome);
    }

    [Fact]
    public void Manager_TakesSomeoneOffATask_ClearsTheLock()
    {
        var outcome = TaskAssignmentPolicy.Decide(Boss, true, Me, Boss, true, null);

        Assert.Equal(new AssignmentOutcome(null, false), outcome);
    }

    [Fact]
    public void Manager_SavesTheTaskWithoutChangingTheAssignee_KeepsTheLockAndTheAssigner()
    {
        var outcome = TaskAssignmentPolicy.Decide(Other, true, Me, Boss, true, Me);

        Assert.Equal(new AssignmentOutcome(Boss, true), outcome);
    }

    // ---- Member ----

    [Fact]
    public void Member_EditsTheirOwnLockedTask_KeepsTheLock()
    {
        var outcome = TaskAssignmentPolicy.Decide(Me, false, Me, Boss, true, Me);

        Assert.Equal(new AssignmentOutcome(Boss, true), outcome);
    }

    [Fact]
    public void Member_TakesAnUnassignedTask_IsNotLocked()
    {
        var outcome = TaskAssignmentPolicy.Decide(Me, false, null, null, false, Me);

        Assert.Equal(new AssignmentOutcome(null, false), outcome);
    }

    [Fact]
    public void Member_GivesBackATaskTheyTookThemselves_Works()
    {
        var outcome = TaskAssignmentPolicy.Decide(Me, false, Me, null, false, null);

        Assert.Equal(new AssignmentOutcome(null, false), outcome);
    }

    [Fact]
    public void Member_GivesBackATaskAnOwnerAssigned_IsRefusedWithTheReason()
    {
        var ex = Assert.Throws<ForbiddenAccessException>(() => TaskAssignmentPolicy.Decide(Me, false, Me, Boss, true, null));

        Assert.Equal(TaskAssignmentPolicy.LockedMessage, ex.Message);
    }

    [Fact]
    public void Member_HandsTheirTaskToSomeoneElse_IsRefused()
    {
        Assert.Throws<ForbiddenAccessException>(() => TaskAssignmentPolicy.Decide(Me, false, Me, null, false, Other));
    }

    [Fact]
    public void Member_TouchesATaskOfSomeoneElse_IsRefused()
    {
        Assert.Throws<ForbiddenAccessException>(() => TaskAssignmentPolicy.Decide(Me, false, Other, null, false, Other));
        Assert.Throws<ForbiddenAccessException>(() => TaskAssignmentPolicy.Decide(Me, false, Other, null, false, Me));
        Assert.Throws<ForbiddenAccessException>(() => TaskAssignmentPolicy.Decide(Me, false, Other, null, false, null));
    }

    [Fact]
    public void Member_EditsAnUnassignedTaskWithoutTakingIt_IsRefused()
    {
        Assert.Throws<ForbiddenAccessException>(() => TaskAssignmentPolicy.Decide(Me, false, null, null, false, null));
    }

    [Fact]
    public void Member_GivesAnUnassignedTaskToSomeoneElse_IsRefused()
    {
        Assert.Throws<ForbiddenAccessException>(() => TaskAssignmentPolicy.Decide(Me, false, null, null, false, Other));
    }

    // ---- new items ----

    [Fact]
    public void ForNewItem_ManagerAssignsToSomeoneElse_IsLocked()
    {
        Assert.Equal(new AssignmentOutcome(Boss, true), TaskAssignmentPolicy.ForNewItem(Boss, true, Me));
    }

    [Fact]
    public void ForNewItem_ManagerKeepsItOrLeavesItOpen_IsNotLocked()
    {
        Assert.Equal(new AssignmentOutcome(null, false), TaskAssignmentPolicy.ForNewItem(Boss, true, Boss));
        Assert.Equal(new AssignmentOutcome(null, false), TaskAssignmentPolicy.ForNewItem(Boss, true, null));
    }

    [Fact]
    public void ForNewItem_MemberCreatesForThemselves_IsNotLocked()
    {
        Assert.Equal(new AssignmentOutcome(null, false), TaskAssignmentPolicy.ForNewItem(Me, false, Me));
    }
}
