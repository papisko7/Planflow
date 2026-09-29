using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Teams.Commands.AddTeamMember;
using PlanFlow.Application.Teams.Commands.CreateTeam;
using PlanFlow.Application.Teams.Queries.GetTeamMembers;
using PlanFlow.Application.Teams.Queries.GetUserTeams;
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
        var team = await TestDb.AddTeamAsync(db, (ann, TeamRole.Owner), (bob, TeamRole.Guest));

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

        var dto = await new AddTeamMemberCommandHandler(db)
            .Handle(new AddTeamMemberCommand(team.Id, owner.Id, newUser.Id, TeamRole.Member), CancellationToken.None);

        Assert.Equal("New", dto.UserDisplayName);
        Assert.Equal(2, db.TeamMembers.Count());
    }

    [Fact]
    public async Task AddMember_AsGuest_ThrowsForbidden()
    {
        var db = TestDb.Create();
        var guest = await TestDb.AddUserAsync(db);
        var newUser = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (guest, TeamRole.Guest));

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => new AddTeamMemberCommandHandler(db)
                .Handle(new AddTeamMemberCommand(team.Id, guest.Id, newUser.Id, TeamRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task AddMember_AlreadyMember_ThrowsConflict()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));

        await Assert.ThrowsAsync<ConflictException>(
            () => new AddTeamMemberCommandHandler(db)
                .Handle(new AddTeamMemberCommand(team.Id, owner.Id, member.Id, TeamRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task AddMember_RequesterNotInTeam_ThrowsNotFound()
    {
        var db = TestDb.Create();
        var stranger = await TestDb.AddUserAsync(db);
        var newUser = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => new AddTeamMemberCommandHandler(db)
                .Handle(new AddTeamMemberCommand(team.Id, stranger.Id, newUser.Id, TeamRole.Member), CancellationToken.None));
    }
}
