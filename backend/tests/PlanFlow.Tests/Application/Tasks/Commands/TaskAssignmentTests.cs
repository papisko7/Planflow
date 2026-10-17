using FluentValidation;
using Moq;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Teams.Commands.AddTeamMember;
using PlanFlow.Application.Teams.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

public class TaskAssignmentTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    [Fact]
    public async Task CreateTask_AssigneeInTeam_IsAccepted()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));

        var task = await new CreateTaskCommandHandler(db, Cache, new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new CreateTaskCommand(team.Id, "T", null, member.Id, null, 1, null, owner.Id), CancellationToken.None);

        Assert.Equal(member.Id, task.AssignedUserId);
    }

    [Fact]
    public async Task CreateTask_AssigneeOutsideTeam_ThrowsValidation()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var stranger = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));

        await Assert.ThrowsAsync<ValidationException>(() => new CreateTaskCommandHandler(db, Cache, new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new CreateTaskCommand(team.Id, "T", null, stranger.Id, null, 1, null, owner.Id), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateTask_AssigneeOutsideTeam_ThrowsValidation()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var stranger = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        var task = new CalendarItem { TeamId = team.Id, Title = "T", AssignedUserId = owner.Id };
        db.CalendarItems.Add(task);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => new UpdateTaskCommandHandler(db, Cache, new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new UpdateTaskCommand(task.Id, "T", null, TaskStatus.Todo, stranger.Id, null, 1, null, null, null, owner.Id),
            CancellationToken.None));
    }

    [Fact]
    public async Task AddMember_ToPersonalTeam_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        await PersonalTeam.EnsureAsync(db, owner.Id, CancellationToken.None);
        await db.SaveChangesAsync();
        var personal = db.Teams.Single(t => t.IsPersonal);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new AddTeamMemberCommandHandler(db, new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new AddTeamMemberCommand(personal.Id, owner.Id, other.Email, TeamRole.Member), CancellationToken.None));
    }
}
