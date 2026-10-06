using Moq;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Teams.Commands.AddTeamMember;
using PlanFlow.Application.Teams.Commands.ChangeMemberRole;
using PlanFlow.Application.Teams.Commands.DeleteTeam;
using PlanFlow.Application.Teams.Commands.RemoveTeamMember;
using PlanFlow.Application.Teams.Commands.UpdateTeamNotificationSettings;
using PlanFlow.Application.Teams.Queries.GetUserTeams;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;

namespace PlanFlow.Tests.Application.Teams;

/// <summary>The e-mails sent for team events (added, role changed, removed, team deleted) and the new role-change command.</summary>
public class TeamNotificationTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    private static AddTeamMemberCommandHandler AddHandler(PlanFlowDbContext db) => new(db, new NotificationService(db));
    private static RemoveTeamMemberCommandHandler RemoveHandler(PlanFlowDbContext db) => new(db, Cache, new NotificationService(db));
    private static DeleteTeamCommandHandler DeleteHandler(PlanFlowDbContext db) => new(db, Cache, new NotificationService(db));
    private static ChangeMemberRoleCommandHandler RoleHandler(PlanFlowDbContext db) => new(db, new NotificationService(db));

    // ---- added ----

    [Fact]
    public async Task AddMember_MailsTheNewMemberWithTeamRoleAndWhoAddedThem()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var newcomer = await TestDb.AddUserAsync(db, "Nina");
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));

        await AddHandler(db).Handle(new AddTeamMemberCommand(team.Id, owner.Id, newcomer.Email, TeamRole.Admin), CancellationToken.None);

        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(NotificationEvent.AddedToTeam, mail.Event);
        Assert.Equal(newcomer.Email, mail.ToEmail);
        Assert.Contains("Olga", mail.TextBody);
        Assert.Contains("Admin", mail.TextBody);
        Assert.Contains("Team", mail.Subject);
    }

    [Fact]
    public async Task AddMember_NewMemberSwitchedTeamMailsOff_GetsNoMailButIsStillAdded()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var newcomer = await TestDb.AddUserAsync(db);
        db.NotificationPreferences.Add(new NotificationPreference { UserId = newcomer.Id, TeamMembership = false });
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));

        await AddHandler(db).Handle(new AddTeamMemberCommand(team.Id, owner.Id, newcomer.Email, TeamRole.Member), CancellationToken.None);

        Assert.Empty(db.EmailOutbox);
        Assert.Equal(2, db.TeamMembers.Count());
    }

    // ---- removed ----

    [Fact]
    public async Task RemoveMember_ByOwner_MailsTheRemovedMember()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));

        await RemoveHandler(db).Handle(new RemoveTeamMemberCommand(team.Id, owner.Id, member.Id), CancellationToken.None);

        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(NotificationEvent.RemovedFromTeam, mail.Event);
        Assert.Equal(member.Email, mail.ToEmail);
        Assert.Contains("Olga", mail.TextBody);
    }

    [Fact]
    public async Task RemoveMember_LeavingYourself_SendsNoMail()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));

        await RemoveHandler(db).Handle(new RemoveTeamMemberCommand(team.Id, member.Id, member.Id), CancellationToken.None);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task RemoveMember_ForbiddenAttempt_QueuesNothing()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member), (other, TeamRole.Member));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            RemoveHandler(db).Handle(new RemoveTeamMemberCommand(team.Id, member.Id, other.Id), CancellationToken.None));

        Assert.Empty(db.EmailOutbox);
    }

    // ---- team deleted ----

    [Fact]
    public async Task DeleteTeam_MailsEveryoneExceptTheOwnerWhoDeletedIt()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var admin = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin, TeamRole.Admin), (member, TeamRole.Member));

        await DeleteHandler(db).Handle(new DeleteTeamCommand(team.Id, owner.Id), CancellationToken.None);

        Assert.Equal(2, db.EmailOutbox.Count());
        Assert.All(db.EmailOutbox, m => Assert.Equal(NotificationEvent.TeamDeleted, m.Event));
        Assert.DoesNotContain(db.EmailOutbox, m => m.ToEmail == owner.Email);
        Assert.Empty(db.Teams.Where(t => t.Id == team.Id));
    }

    // ---- role change ----

    [Fact]
    public async Task ChangeRole_OwnerPromotesMemberToAdmin_MailsThemWithOldAndNewRole()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));

        var dto = await RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, owner.Id, member.Id, TeamRole.Admin), CancellationToken.None);

        Assert.Equal(TeamRole.Admin, dto.Role);
        Assert.Equal(TeamRole.Admin, db.TeamMembers.Single(m => m.UserId == member.Id).Role);
        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(NotificationEvent.TeamRoleChanged, mail.Event);
        Assert.Equal(member.Email, mail.ToEmail);
        Assert.Contains("Member", mail.TextBody);
        Assert.Contains("Admin", mail.TextBody);
        Assert.Contains("Olga", mail.TextBody);
    }

    [Fact]
    public async Task ChangeRole_OwnerDemotesAdminToMember()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var admin = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin, TeamRole.Admin));

        await RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, owner.Id, admin.Id, TeamRole.Member), CancellationToken.None);

        Assert.Equal(TeamRole.Member, db.TeamMembers.Single(m => m.UserId == admin.Id).Role);
        Assert.Single(db.EmailOutbox);
    }

    [Fact]
    public async Task ChangeRole_SameRole_ChangesNothingAndSendsNothing()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member, TeamRole.Member));

        await RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, owner.Id, member.Id, TeamRole.Member), CancellationToken.None);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task ChangeRole_AdminCannotGrantAdmin()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var admin = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin, TeamRole.Admin), (member, TeamRole.Member));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, admin.Id, member.Id, TeamRole.Admin), CancellationToken.None));

        Assert.Equal(TeamRole.Member, db.TeamMembers.Single(m => m.UserId == member.Id).Role);
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task ChangeRole_AdminCannotDemoteAnotherAdmin()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var admin1 = await TestDb.AddUserAsync(db);
        var admin2 = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin1, TeamRole.Admin), (admin2, TeamRole.Admin));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, admin1.Id, admin2.Id, TeamRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeRole_MemberHasNoPermission()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var member1 = await TestDb.AddUserAsync(db);
        var member2 = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (member1, TeamRole.Member), (member2, TeamRole.Member));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, member1.Id, member2.Id, TeamRole.Admin), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeRole_OwnerRoleIsUntouchable()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var admin = await TestDb.AddUserAsync(db);
        var member = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin, TeamRole.Admin), (member, TeamRole.Member));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>   // demote the owner
            RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, admin.Id, owner.Id, TeamRole.Member), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>   // make a second owner
            RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, owner.Id, member.Id, TeamRole.Owner), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeRole_CannotChangeYourOwnRole()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var admin = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin, TeamRole.Admin));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, admin.Id, admin.Id, TeamRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeRole_UnknownMember_NotFound()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            RoleHandler(db).Handle(new ChangeMemberRoleCommand(team.Id, owner.Id, Guid.NewGuid(), TeamRole.Admin), CancellationToken.None));
    }

    // ---- per-team mute ----

    [Fact]
    public async Task TeamMute_IsSavedPerMember_AndReturnedByGetUserTeams()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db);
        var bob = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (ann, TeamRole.Owner), (bob, TeamRole.Member));

        await new UpdateTeamNotificationSettingsCommandHandler(db)
            .Handle(new UpdateTeamNotificationSettingsCommand(team.Id, bob.Id, true, true), CancellationToken.None);

        var bobsTeam = Assert.Single(await new GetUserTeamsQueryHandler(db).Handle(new GetUserTeamsQuery(bob.Id), CancellationToken.None));
        var annsTeam = Assert.Single(await new GetUserTeamsQueryHandler(db).Handle(new GetUserTeamsQuery(ann.Id), CancellationToken.None));
        Assert.True(bobsTeam.MuteNotifications);
        Assert.True(bobsTeam.MuteChat);
        Assert.False(annsTeam.MuteNotifications); // Ann is unaffected
    }

    [Fact]
    public async Task TeamMute_NotAMember_NotFound()
    {
        var db = TestDb.Create();
        var ann = await TestDb.AddUserAsync(db);
        var stranger = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (ann, TeamRole.Owner));

        await Assert.ThrowsAsync<NotFoundException>(() => new UpdateTeamNotificationSettingsCommandHandler(db)
            .Handle(new UpdateTeamNotificationSettingsCommand(team.Id, stranger.Id, true, false), CancellationToken.None));
    }
}
