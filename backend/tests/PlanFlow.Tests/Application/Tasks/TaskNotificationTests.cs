using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.DeleteTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks;

/// <summary>The e-mails sent when a task is created, edited, reassigned or deleted — and who is deliberately NOT mailed.</summary>
public class TaskNotificationTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    private static Task<PlanFlow.Application.Tasks.Common.TaskDto> Create(
        PlanFlowDbContext db, Guid teamId, Guid actorId, Guid? assignee, string title = "Write report") =>
        new CreateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new CreateTaskCommand(teamId, title, null, assignee, null, 5, null, actorId), CancellationToken.None);

    private static Task Update(
        PlanFlowDbContext db, CalendarItem task, Guid actorId,
        string? title = null, TaskStatus? status = null, Guid? assignee = null, bool keepAssignee = true, DateTime? deadline = null, int? impact = 5) =>
        new UpdateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new UpdateTaskCommand(
                task.Id, title ?? task.Title, task.Description, status ?? task.Status,
                keepAssignee ? task.AssignedUserId : assignee,
                deadline ?? task.DeadlineUtc, impact, task.BlockedByTaskId, null, null, actorId),
            CancellationToken.None);

    private static async Task<(PlanFlowDbContext Db, User Owner, User Admin, User Member, Team Team)> TeamOfThree()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var admin = await TestDb.AddUserAsync(db, "Adam");
        var member = await TestDb.AddUserAsync(db, "Mia");
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (admin, TeamRole.Admin), (member, TeamRole.Member));
        return (db, owner, admin, member, team);
    }

    /// <summary>Forget the mails produced while setting the scene, so a test only sees what the action under test sends.</summary>
    private static void ClearOutbox(PlanFlowDbContext db)
    {
        db.EmailOutbox.RemoveRange(db.EmailOutbox.ToList());
        db.SaveChanges();
    }

    // ---- created ----

    [Fact]
    public async Task Create_AssignedToSomeoneElse_MailsTheAssignee()
    {
        var (db, owner, _, member, team) = await TeamOfThree();

        await Create(db, team.Id, owner.Id, member.Id);

        var mail = Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.TaskAssignedToYou);
        Assert.Equal(member.Email, mail.ToEmail);
        Assert.Contains("Olga", mail.TextBody);
        Assert.Contains("Write report", mail.TextBody);
    }

    [Fact]
    public async Task Create_AssignedToYourself_MailsNobodyAboutIt()
    {
        var (db, _, _, member, team) = await TeamOfThree();

        await Create(db, team.Id, member.Id, member.Id);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Create_ByOwnerOrAdmin_AnnouncesToMembersWhoOptedIn_NotToTheAssigneeTwice()
    {
        var (db, owner, admin, member, team) = await TeamOfThree();
        db.NotificationPreferences.Add(new NotificationPreference { UserId = admin.Id, TeamTasks = true });
        db.NotificationPreferences.Add(new NotificationPreference { UserId = member.Id, TeamTasks = true });
        await db.SaveChangesAsync();

        await Create(db, team.Id, owner.Id, member.Id);

        // Mia gets the specific "assigned to you" mail only; Adam (opted in) gets the team announcement; Olga did it herself.
        Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.TaskAssignedToYou && m.ToEmail == member.Email);
        Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.TeamTaskAdded && m.ToEmail == admin.Email);
        Assert.DoesNotContain(db.EmailOutbox, m => m.Event == NotificationEvent.TeamTaskAdded && m.ToEmail == member.Email);
        Assert.DoesNotContain(db.EmailOutbox, m => m.ToEmail == owner.Email);
    }

    [Fact]
    public async Task Create_TeamAnnouncement_IsOffByDefault()
    {
        var (db, owner, _, member, team) = await TeamOfThree();

        await Create(db, team.Id, owner.Id, member.Id);

        Assert.DoesNotContain(db.EmailOutbox, m => m.Event == NotificationEvent.TeamTaskAdded);
    }

    [Fact]
    public async Task Create_ByRegularMember_IsNotAnnouncedToTheTeam()
    {
        var (db, owner, admin, member, team) = await TeamOfThree();
        db.NotificationPreferences.Add(new NotificationPreference { UserId = owner.Id, TeamTasks = true });
        db.NotificationPreferences.Add(new NotificationPreference { UserId = admin.Id, TeamTasks = true });
        await db.SaveChangesAsync();

        await Create(db, team.Id, member.Id, member.Id);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Create_TeamMutedByRecipient_StaysSilent()
    {
        var (db, owner, _, member, team) = await TeamOfThree();
        db.TeamMembers.Single(m => m.UserId == member.Id).MuteNotifications = true;
        await db.SaveChangesAsync();

        await Create(db, team.Id, owner.Id, member.Id);

        Assert.Empty(db.EmailOutbox);
    }

    // ---- updated ----

    [Fact]
    public async Task Update_ByAnotherUser_MailsTheAssigneeWhatChanged()
    {
        var (db, owner, _, member, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, member.Id);
        ClearOutbox(db);
        var task = db.CalendarItems.Single();

        await Update(db, task, owner.Id, title: "Write final report", status: TaskStatus.InProgress, deadline: DateTime.UtcNow.AddDays(3));

        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(NotificationEvent.TaskChanged, mail.Event);
        Assert.Equal(member.Email, mail.ToEmail);
        Assert.Contains("Write final report", mail.TextBody);
        Assert.Contains("status Backlog → InProgress", mail.TextBody);
        Assert.Contains("deadline none →", mail.TextBody);
    }

    [Fact]
    public async Task Update_ByTheAssigneeThemselves_SendsNothing()
    {
        var (db, owner, _, member, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, member.Id);
        ClearOutbox(db);
        await db.SaveChangesAsync();

        await Update(db, db.CalendarItems.Single(), member.Id, status: TaskStatus.Completed);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Update_NothingMeaningfulChanged_SendsNothing()
    {
        var (db, owner, _, member, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, member.Id);
        ClearOutbox(db);
        await db.SaveChangesAsync();

        await Update(db, db.CalendarItems.Single(), owner.Id); // identical values

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Update_TaskWithoutAssignee_SendsNothing()
    {
        var (db, owner, _, _, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, null);

        await Update(db, db.CalendarItems.Single(), owner.Id, title: "Renamed");

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Update_Reassigned_MailsNewAssigneeAndTellsTheOldOne()
    {
        var (db, owner, admin, member, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, member.Id);
        ClearOutbox(db);
        await db.SaveChangesAsync();

        await Update(db, db.CalendarItems.Single(), owner.Id, assignee: admin.Id, keepAssignee: false);

        Assert.Equal(2, db.EmailOutbox.Count());
        Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.TaskAssignedToYou && m.ToEmail == admin.Email);
        Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.TaskUnassigned && m.ToEmail == member.Email);
    }

    [Fact]
    public async Task Update_AssigneeTakesTheirOwnTaskOff_OnlyOwnerAndAdminAreToldItIsBackInThePool()
    {
        var (db, owner, admin, member, team) = await TeamOfThree();
        await Create(db, team.Id, member.Id, member.Id);
        ClearOutbox(db);
        await db.SaveChangesAsync();

        await Update(db, db.CalendarItems.Single(), member.Id, assignee: null, keepAssignee: false);

        // Mia gave it back: she hears nothing about her own action, the ones who hand out work are told.
        Assert.DoesNotContain(db.EmailOutbox, m => m.ToEmail == member.Email);
        Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.TaskReturnedToPool && m.ToEmail == owner.Email);
        Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.TaskReturnedToPool && m.ToEmail == admin.Email);
    }

    [Fact]
    public async Task Update_OwnerTakesSomeonesTaskOff_NobodyElseIsToldItIsBackInThePool()
    {
        var (db, owner, admin, member, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, member.Id);
        ClearOutbox(db);
        await db.SaveChangesAsync();

        await Update(db, db.CalendarItems.Single(), owner.Id, assignee: null, keepAssignee: false);

        Assert.DoesNotContain(db.EmailOutbox, m => m.Event == NotificationEvent.TaskReturnedToPool);
        Assert.Single(db.EmailOutbox, m => m.Event == NotificationEvent.TaskUnassigned && m.ToEmail == member.Email);
    }

    [Fact]
    public async Task Update_AssigneeSwitchedTaskChangesOff_GetsNoMail()
    {
        var (db, owner, _, member, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, member.Id);
        ClearOutbox(db);
        db.NotificationPreferences.Add(new NotificationPreference { UserId = member.Id, TaskChanges = false });
        await db.SaveChangesAsync();

        await Update(db, db.CalendarItems.Single(), owner.Id, title: "Renamed");

        Assert.Empty(db.EmailOutbox);
    }

    // ---- deleted ----

    [Fact]
    public async Task Delete_ByAnotherUser_MailsTheAssignee()
    {
        var (db, owner, _, member, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, member.Id);
        ClearOutbox(db);
        await db.SaveChangesAsync();
        var task = db.CalendarItems.Single();

        await new DeleteTaskCommandHandler(db, Cache, new NotificationService(db))
            .Handle(new DeleteTaskCommand(task.Id, owner.Id), CancellationToken.None);

        var mail = Assert.Single(db.EmailOutbox);
        Assert.Equal(NotificationEvent.TaskDeleted, mail.Event);
        Assert.Equal(member.Email, mail.ToEmail);
        Assert.Contains("Olga", mail.TextBody);
        Assert.Empty(db.CalendarItems);
    }

    [Fact]
    public async Task Delete_ByTheAssignee_SendsNothing()
    {
        var (db, owner, _, member, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, member.Id);
        ClearOutbox(db);
        await db.SaveChangesAsync();
        var task = db.CalendarItems.Single();

        await new DeleteTaskCommandHandler(db, Cache, new NotificationService(db))
            .Handle(new DeleteTaskCommand(task.Id, member.Id), CancellationToken.None);

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task Delete_UnassignedTask_SendsNothing()
    {
        var (db, owner, _, _, team) = await TeamOfThree();
        await Create(db, team.Id, owner.Id, null);

        await new DeleteTaskCommandHandler(db, Cache, new NotificationService(db))
            .Handle(new DeleteTaskCommand(db.CalendarItems.Single().Id, owner.Id), CancellationToken.None);

        Assert.Empty(db.EmailOutbox);
    }
}
