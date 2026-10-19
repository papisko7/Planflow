using Moq;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Application.Teams.Commands.RemoveTeamMember;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

/// <summary>
/// Step 7.5.10b: an item an Owner/Admin assigned is locked to its assignee (who cannot give it back, but can still work on it and
/// invite co-contractors); the lock is stored with who assigned it, follows the role at that moment and is cleared when the assignee changes.
/// </summary>
public class TaskAssignmentLockTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    private sealed record Setup(PlanFlowDbContext Db, User Owner, User Admin, User Mia, User Max, Team Team);

    private static async Task<Setup> SetUpAsync()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var admin = await TestDb.AddUserAsync(db, "Adam");
        var mia = await TestDb.AddUserAsync(db, "Mia");
        var max = await TestDb.AddUserAsync(db, "Max");
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin, TeamRole.Admin), (mia, TeamRole.Member), (max, TeamRole.Member));
        return new Setup(db, owner, admin, mia, max, team);
    }

    private static Task<TaskDto> Create(Setup s, User by, Guid? assignee, bool byManager) =>
        new CreateTaskCommandHandler(s.Db, Cache, new NotificationService(s.Db)).Handle(
            new CreateTaskCommand(s.Team.Id, "Report", null, assignee, null, 5, null, by.Id, CreatorIsManager: byManager), CancellationToken.None);

    private static Task<TaskDto> Update(
        Setup s, Guid taskId, User by, Guid? assignee, bool byManager, IReadOnlyList<Guid>? participants = null) =>
        new UpdateTaskCommandHandler(s.Db, Cache, new NotificationService(s.Db)).Handle(
            new UpdateTaskCommand(
                taskId, "Report", null, TaskStatus.Backlog, assignee, null, 5, null, null, null, by.Id,
                ParticipantUserIds: participants, CallerIsManager: byManager),
            CancellationToken.None);

    // ---- created ----

    [Fact]
    public async Task Create_OwnerAssignsToMember_IsLockedAndRemembersWho()
    {
        var s = await SetUpAsync();

        var task = await Create(s, s.Owner, s.Mia.Id, byManager: true);

        Assert.True(task.IsAssignmentLocked);
        Assert.Equal(s.Owner.Id, task.AssignedByUserId);
        Assert.True(s.Db.CalendarItems.Single().IsAssignmentLocked);
    }

    [Fact]
    public async Task Create_MemberCreatesOwnTask_IsNotLocked()
    {
        var s = await SetUpAsync();

        var task = await Create(s, s.Mia, s.Mia.Id, byManager: false);

        Assert.False(task.IsAssignmentLocked);
        Assert.Null(task.AssignedByUserId);
    }

    [Fact]
    public async Task Create_OwnerLeavesItUnassignedOrKeepsIt_IsNotLocked()
    {
        var s = await SetUpAsync();

        var open = await Create(s, s.Owner, null, byManager: true);
        var own = await Create(s, s.Owner, s.Owner.Id, byManager: true);

        Assert.False(open.IsAssignmentLocked);
        Assert.False(own.IsAssignmentLocked);
    }

    // ---- updated ----

    [Fact]
    public async Task Update_MemberGivesBackALockedTask_IsForbiddenAndNothingChanges()
    {
        var s = await SetUpAsync();
        var task = await Create(s, s.Owner, s.Mia.Id, byManager: true);

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => Update(s, task.Id, s.Mia, null, byManager: false));

        Assert.Equal(TaskAssignmentPolicy.LockedMessage, ex.Message);
        var stored = s.Db.CalendarItems.Single();
        Assert.Equal(s.Mia.Id, stored.AssignedUserId);
        Assert.True(stored.IsAssignmentLocked);
    }

    [Fact]
    public async Task Update_MemberGivesBackATaskTheyTookThemselves_Works()
    {
        var s = await SetUpAsync();
        var task = await Create(s, s.Mia, s.Mia.Id, byManager: false);

        var result = await Update(s, task.Id, s.Mia, null, byManager: false);

        Assert.Null(result.AssignedUserId);
    }

    [Fact]
    public async Task Update_MemberClaimsAnOpenTask_IsNotLocked()
    {
        var s = await SetUpAsync();
        var task = await Create(s, s.Owner, null, byManager: true);

        var result = await Update(s, task.Id, s.Mia, s.Mia.Id, byManager: false);

        Assert.Equal(s.Mia.Id, result.AssignedUserId);
        Assert.False(result.IsAssignmentLocked);
        Assert.Null(result.AssignedByUserId);
    }

    [Fact]
    public async Task Update_MemberEditsALockedTaskOfTheirs_KeepsTheLock()
    {
        var s = await SetUpAsync();
        var task = await Create(s, s.Owner, s.Mia.Id, byManager: true);

        var result = await Update(s, task.Id, s.Mia, s.Mia.Id, byManager: false);

        Assert.True(result.IsAssignmentLocked);
        Assert.Equal(s.Owner.Id, result.AssignedByUserId);
    }

    [Fact]
    public async Task Update_AssigneeOfALockedTask_CanInviteCoContractors()
    {
        var s = await SetUpAsync();
        var task = await Create(s, s.Owner, s.Mia.Id, byManager: true);

        var result = await Update(s, task.Id, s.Mia, s.Mia.Id, byManager: false, participants: new[] { s.Max.Id });

        Assert.Equal(new[] { s.Max.Id }, result.ParticipantUserIds);
        Assert.True(result.IsAssignmentLocked);
    }

    [Fact]
    public async Task Update_AdminMovesALockedTaskToAnotherMember_LocksItUnderTheAdmin()
    {
        var s = await SetUpAsync();
        var task = await Create(s, s.Owner, s.Mia.Id, byManager: true);

        var result = await Update(s, task.Id, s.Admin, s.Max.Id, byManager: true);

        Assert.Equal(s.Max.Id, result.AssignedUserId);
        Assert.True(result.IsAssignmentLocked);
        Assert.Equal(s.Admin.Id, result.AssignedByUserId);
    }

    [Fact]
    public async Task Update_OwnerTakesTheTaskOffTheAssignee_ClearsTheLock()
    {
        var s = await SetUpAsync();
        var task = await Create(s, s.Owner, s.Mia.Id, byManager: true);

        var result = await Update(s, task.Id, s.Owner, null, byManager: true);

        Assert.Null(result.AssignedUserId);
        Assert.False(result.IsAssignmentLocked);
        Assert.Null(result.AssignedByUserId);
    }

    [Fact]
    public async Task Update_AssignerIsDemotedLater_TheLockStaysBecauseItFollowsTheRoleAtAssignment()
    {
        var s = await SetUpAsync();
        var task = await Create(s, s.Admin, s.Mia.Id, byManager: true);
        s.Db.TeamMembers.Single(m => m.UserId == s.Admin.Id).Role = TeamRole.Member;
        await s.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Update(s, task.Id, s.Mia, null, byManager: false));
    }

    [Fact]
    public async Task Update_MemberTriesToTakeALockedTaskOverFromAnotherMember_IsForbidden()
    {
        var s = await SetUpAsync();
        var task = await Create(s, s.Owner, s.Mia.Id, byManager: true);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Update(s, task.Id, s.Max, s.Max.Id, byManager: false));
    }

    // ---- the lock does not outlive the assignment ----

    [Fact]
    public async Task RemoveMember_UnassignsTheirTasksAndClearsTheLock()
    {
        var s = await SetUpAsync();
        await Create(s, s.Owner, s.Mia.Id, byManager: true);

        await new RemoveTeamMemberCommandHandler(s.Db, Cache, new NotificationService(s.Db))
            .Handle(new RemoveTeamMemberCommand(s.Team.Id, s.Owner.Id, s.Mia.Id), CancellationToken.None);

        var stored = s.Db.CalendarItems.Single();
        Assert.Null(stored.AssignedUserId);
        Assert.Null(stored.AssignedByUserId);
        Assert.False(stored.IsAssignmentLocked);
    }

    [Fact]
    public void NextOccurrenceOfARepeatingTask_StaysWithTheSameAssigneeOnTheSameTerms()
    {
        var finished = new CalendarItem
        {
            TeamId = Guid.NewGuid(), Title = "Weekly", TaskType = TaskType.Task, Status = TaskStatus.Completed, IsRecurring = true,
            RecurrenceIntervalHours = 24 * 7, DeadlineUtc = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc),
            AssignedUserId = Guid.NewGuid(), AssignedByUserId = Guid.NewGuid(), IsAssignmentLocked = true
        };

        var next = RecurrenceScheduler.CreateNextInstance(finished, new DateTime(2026, 10, 19, 13, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(next);
        Assert.Equal(finished.AssignedUserId, next!.AssignedUserId);
        Assert.Equal(finished.AssignedByUserId, next.AssignedByUserId);
        Assert.True(next.IsAssignmentLocked);
    }
}
