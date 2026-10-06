using Moq;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Teams.Commands.DeleteTeam;
using PlanFlow.Application.Teams.Commands.UpdateTeam;
using PlanFlow.Application.Teams.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Tests.Application.Teams;

public class TeamCrudTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    [Theory]
    [InlineData(TeamRole.Owner)]
    [InlineData(TeamRole.Admin)]
    public async Task UpdateTeam_OwnerOrAdmin_RenamesTeam(TeamRole role)
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (user, role));

        var dto = await new UpdateTeamCommandHandler(db).Handle(
            new UpdateTeamCommand(team.Id, user.Id, "  Renamed ", "About"), CancellationToken.None);

        Assert.Equal("Renamed", dto.Name);
        Assert.Equal("About", db.Teams.Single(t => t.Id == team.Id).Description);
    }

    [Fact]
    public async Task UpdateTeam_Member_IsForbidden()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new UpdateTeamCommandHandler(db).Handle(
            new UpdateTeamCommand(team.Id, member.Id, "Hack", null), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateTeam_PersonalTeam_IsForbidden()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        await PersonalTeam.EnsureAsync(db, user.Id, CancellationToken.None);
        await db.SaveChangesAsync();
        var personal = db.Teams.Single(t => t.IsPersonal);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new UpdateTeamCommandHandler(db).Handle(
            new UpdateTeamCommand(personal.Id, user.Id, "Mine", null), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteTeam_Owner_RemovesTeam()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));

        await new DeleteTeamCommandHandler(db, Cache, new PlanFlow.Application.Notifications.NotificationService(db)).Handle(new DeleteTeamCommand(team.Id, owner.Id), CancellationToken.None);

        Assert.Empty(db.Teams);
    }

    [Theory]
    [InlineData(TeamRole.Admin)]
    [InlineData(TeamRole.Member)]
    public async Task DeleteTeam_NonOwner_IsForbidden(TeamRole role)
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (other, role));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new DeleteTeamCommandHandler(db, Cache, new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new DeleteTeamCommand(team.Id, other.Id), CancellationToken.None));
        Assert.Single(db.Teams);
    }

    [Fact]
    public async Task DeleteTeam_PersonalTeam_IsForbidden()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        await PersonalTeam.EnsureAsync(db, user.Id, CancellationToken.None);
        await db.SaveChangesAsync();
        var personal = db.Teams.Single(t => t.IsPersonal);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new DeleteTeamCommandHandler(db, Cache, new PlanFlow.Application.Notifications.NotificationService(db)).Handle(
            new DeleteTeamCommand(personal.Id, user.Id), CancellationToken.None));
    }
}
