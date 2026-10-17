using Moq;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Teams.Commands.AddTeamMember;
using PlanFlow.Application.Teams.Commands.CreateTeam;
using PlanFlow.Application.Teams.Commands.RemoveTeamMember;
using PlanFlow.Application.Teams.Queries.GetTeamMembers;
using PlanFlow.Application.Teams.Queries.GetUserTeams;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Tests.Application.Teams;

public class TeamHandlerTests
{
    [Fact]
    public async Task CreateTeam_MakesCreatorTheOwner()
    {
        var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);

        var dto = await new CreateTeamCommandHandler(db)
            .Handle(new CreateTeamCommand("Alpha", null, user.Id), CancellationToken.None);

        var membership = db.TeamMembers.Single();
        Assert.Equal(dto.Id, membership.TeamId);
        Assert.Equal(TeamRole.Owner, membership.Role);
    }

    [Fact]
    public async Task GetUserTeams_ReturnsOnlyOwnTeamsWithMemberCount()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var mine = await TestDb.AddTeamAsync(db, (me, TeamRole.Member), (other, TeamRole.Member));
        await TestDb.AddTeamAsync(db, (other, TeamRole.Owner));

        var teams = await new GetUserTeamsQueryHandler(db)
            .Handle(new GetUserTeamsQuery(me.Id), CancellationToken.None);

        var single = Assert.Single(teams);
        Assert.Equal(mine.Id, single.Id);
        Assert.Equal(2, single.MemberCount);
    }

    [Fact]
    public async Task GetTeamMembers_AsMember_ReturnsRosterWithNames()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db, "Ann");
        var bob = await TestDb.AddUserAsync(db, "Bob");
        var team = await TestDb.AddTeamAsync(db, (ann, TeamRole.Owner), (bob, TeamRole.Member));

        var members = await new GetTeamMembersQueryHandler(db)
            .Handle(new GetTeamMembersQuery(team.Id, bob.Id), CancellationToken.None);

        Assert.Equal(2, members.Count);
        Assert.Contains(members, m => m.UserDisplayName == "Ann" && m.Role == TeamRole.Owner);
    }

    [Fact]
    public async Task GetTeamMembers_AsOutsider_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db);
        var outsider = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (ann, TeamRole.Owner));

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => new GetTeamMembersQueryHandler(db)
                .Handle(new GetTeamMembersQuery(team.Id, outsider.Id), CancellationToken.None));
    }

    [Fact]
    public async Task AddMember_AsOwner_AddsMember()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var newUser = await TestDb.AddUserAsync(db, "New");
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));

        var dto = await new AddTeamMemberCommandHandler(db, new PlanFlow.Application.Notifications.NotificationService(db))
            .Handle(new AddTeamMemberCommand(team.Id, owner.Id, newUser.Email.ToUpper(), TeamRole.Member), CancellationToken.None);

        Assert.Equal("New", dto.UserDisplayName);
        Assert.Equal(2, db.TeamMembers.Count());
    }

    [Fact]
    public async Task AddMember_AsMember_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var member = await TestDb.AddUserAsync(db);
        var newUser = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (member, TeamRole.Member));

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => new AddTeamMemberCommandHandler(db, new PlanFlow.Application.Notifications.NotificationService(db))
                .Handle(new AddTeamMemberCommand(team.Id, member.Id, newUser.Email, TeamRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task AddMember_AlreadyMember_ThrowsConflict()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));

        await Assert.ThrowsAsync<ConflictException>(
            () => new AddTeamMemberCommandHandler(db, new PlanFlow.Application.Notifications.NotificationService(db))
                .Handle(new AddTeamMemberCommand(team.Id, owner.Id, member.Email, TeamRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task AddMember_RequesterNotInTeam_ThrowsNotFound()
    {
        var db = TestDb.Create();
        var stranger = await TestDb.AddUserAsync(db);
        var newUser = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => new AddTeamMemberCommandHandler(db, new PlanFlow.Application.Notifications.NotificationService(db))
                .Handle(new AddTeamMemberCommand(team.Id, stranger.Id, newUser.Email, TeamRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task AddMember_UnknownEmail_ThrowsNotFound()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));

        await Assert.ThrowsAsync<NotFoundException>(
            () => new AddTeamMemberCommandHandler(db, new PlanFlow.Application.Notifications.NotificationService(db))
                .Handle(new AddTeamMemberCommand(team.Id, owner.Id, "nobody@example.com", TeamRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task AddMember_AsOwnerRole_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var newUser = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => new AddTeamMemberCommandHandler(db, new PlanFlow.Application.Notifications.NotificationService(db))
                .Handle(new AddTeamMemberCommand(team.Id, owner.Id, newUser.Email, TeamRole.Owner), CancellationToken.None));
    }

    [Fact]
    public async Task AddMember_AdminCannotAddAdmin_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var admin = await TestDb.AddUserAsync(db);
        var newUser = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (admin, TeamRole.Admin));

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => new AddTeamMemberCommandHandler(db, new PlanFlow.Application.Notifications.NotificationService(db))
                .Handle(new AddTeamMemberCommand(team.Id, admin.Id, newUser.Email, TeamRole.Admin), CancellationToken.None));
    }

    private static RemoveTeamMemberCommandHandler RemoveHandler(PlanFlow.Infrastructure.Persistence.PlanFlowDbContext db)
        => new(db, new Mock<ICacheService>().Object, new PlanFlow.Application.Notifications.NotificationService(db));

    [Fact]
    public async Task RemoveMember_AsOwner_RemovesMemberAndUnassignsTheirTasks()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));
        var task = new CalendarItem { TeamId = team.Id, Title = "T", AssignedUserId = member.Id };
        db.CalendarItems.Add(task);
        await db.SaveChangesAsync();

        await RemoveHandler(db).Handle(new RemoveTeamMemberCommand(team.Id, owner.Id, member.Id), CancellationToken.None);

        Assert.Single(db.TeamMembers);
        Assert.Null(db.CalendarItems.Single().AssignedUserId);
    }

    [Fact]
    public async Task RemoveMember_AdminRemovingMember_IsAllowed()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var admin = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin, TeamRole.Admin), (member, TeamRole.Member));

        await RemoveHandler(db).Handle(new RemoveTeamMemberCommand(team.Id, admin.Id, member.Id), CancellationToken.None);

        Assert.DoesNotContain(db.TeamMembers, m => m.UserId == member.Id);
    }

    [Fact]
    public async Task RemoveMember_MemberLeavingTeam_IsAllowed()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));

        await RemoveHandler(db).Handle(new RemoveTeamMemberCommand(team.Id, member.Id, member.Id), CancellationToken.None);

        Assert.Equal(owner.Id, db.TeamMembers.Single().UserId);
    }

    [Fact]
    public async Task RemoveMember_MemberRemovingSomeoneElse_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var a = await TestDb.AddUserAsync(db);
        var b = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (a, TeamRole.Member), (b, TeamRole.Member));

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => RemoveHandler(db).Handle(new RemoveTeamMemberCommand(team.Id, a.Id, b.Id), CancellationToken.None));
    }

    [Fact]
    public async Task RemoveMember_Owner_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var admin = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin, TeamRole.Admin));

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => RemoveHandler(db).Handle(new RemoveTeamMemberCommand(team.Id, admin.Id, owner.Id), CancellationToken.None));
    }

    [Fact]
    public async Task RemoveMember_AdminRemovingAdmin_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var admin1 = await TestDb.AddUserAsync(db);
        var admin2 = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin1, TeamRole.Admin), (admin2, TeamRole.Admin));

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => RemoveHandler(db).Handle(new RemoveTeamMemberCommand(team.Id, admin1.Id, admin2.Id), CancellationToken.None));
    }
}
