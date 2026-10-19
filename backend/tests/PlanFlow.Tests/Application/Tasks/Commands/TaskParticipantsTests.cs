using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.DeleteTask;
using PlanFlow.Application.Tasks.Commands.LeaveTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Application.Tasks.Queries.GetTasksByTeam;
using PlanFlow.Application.Teams.Commands.RemoveTeamMember;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

/// <summary>
/// Guests of an event and co-contractors of a task. Only team members can take part, the assignee never also does,
/// and everybody concerned hears about joining, leaving, edits and deletion.
/// </summary>
public class TaskParticipantsTests
{
    private static readonly DateTime NextWeek = DateTime.UtcNow.Date.AddDays(7);

    private sealed record Setup(PlanFlowDbContext Db, Team Team, User Owner, User Anna, User Ben, User Cora);

    private static async Task<Setup> SetUpAsync()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var anna = await TestDb.AddUserAsync(db, "Anna");
        var ben = await TestDb.AddUserAsync(db, "Ben");
        var cora = await TestDb.AddUserAsync(db, "Cora");
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (anna, TeamRole.Member), (ben, TeamRole.Member), (cora, TeamRole.Member));
        return new Setup(db, team, owner, anna, ben, cora);
    }

    private static Mock<ICacheService> CacheMock() => new();

    private static Task<TaskDto> CreateAsync(PlanFlowDbContext db, CreateTaskCommand command) =>
        new CreateTaskCommandHandler(db, CacheMock().Object, new NotificationService(db)).Handle(command, CancellationToken.None);

    private static CreateTaskCommand NewTask(Setup s, Guid? assignee, params Guid[] participants) =>
        new(s.Team.Id, "Report", null, assignee, NextWeek, 5, null, s.Owner.Id, ParticipantUserIds: participants);

    private static CreateTaskCommand NewEvent(Setup s, Guid? assignee, EventTemplate template = EventTemplate.Default, params Guid[] participants) =>
        new(s.Team.Id, "Retro", null, assignee, null, 5, null, s.Owner.Id, TaskType.Event,
            StartUtc: NextWeek.AddHours(9), EndUtc: NextWeek.AddHours(10), EventTemplate: template, ParticipantUserIds: participants);

    private static Task<TaskDto> UpdateAsync(
        PlanFlowDbContext db, CalendarItem item, Guid actor, IReadOnlyList<Guid>? participants, Guid? assignee = null, bool keepAssignee = true, string? title = null) =>
        new UpdateTaskCommandHandler(db, CacheMock().Object, new NotificationService(db)).Handle(
            new UpdateTaskCommand(
                item.Id, title ?? item.Title, item.Description, item.Status, keepAssignee ? item.AssignedUserId : assignee, item.DeadlineUtc,
                item.ImpactScore, item.BlockedByTaskId, null, null, actor, item.TaskType, item.StartUtc, item.EstimatedEffortHours,
                EndUtc: item.EndUtc, ParticipantUserIds: participants),
            CancellationToken.None);

    private static async Task<List<ItemParticipant>> ParticipantsOf(PlanFlowDbContext db, Guid itemId) =>
        await db.ItemParticipants.Where(p => p.CalendarItemId == itemId).ToListAsync();

    private static void ClearNotifications(PlanFlowDbContext db)
    {
        db.EmailOutbox.RemoveRange(db.EmailOutbox.ToList());
        db.Notifications.RemoveRange(db.Notifications.ToList());
        db.SaveChanges();
    }

    // ---- create ----

    [Fact]
    public async Task Create_ATaskWithCoContractors_StoresThemWithTheCoContractorRole()
    {
        var s = await SetUpAsync();

        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id, s.Cora.Id));

        Assert.Equal([s.Ben.Id, s.Cora.Id], dto.ParticipantUserIds);
        Assert.All(await ParticipantsOf(s.Db, dto.Id), p => Assert.Equal(ParticipantRole.CoContractor, p.Role));
    }

    [Fact]
    public async Task Create_AnEventWithGuests_StoresThemWithTheGuestRole()
    {
        var s = await SetUpAsync();

        var dto = await CreateAsync(s.Db, NewEvent(s, s.Anna.Id, participants: [s.Ben.Id]));

        var guest = Assert.Single(await ParticipantsOf(s.Db, dto.Id));
        Assert.Equal(s.Ben.Id, guest.UserId);
        Assert.Equal(ParticipantRole.Guest, guest.Role);
    }

    [Fact]
    public async Task Create_TheAssigneeInTheList_IsQuietlyNotAParticipant()
    {
        var s = await SetUpAsync();

        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Anna.Id, s.Ben.Id));

        Assert.Equal([s.Ben.Id], dto.ParticipantUserIds);
    }

    [Fact]
    public async Task Create_SomeoneOutsideTheTeam_IsRefused()
    {
        var s = await SetUpAsync();
        var stranger = await TestDb.AddUserAsync(s.Db, "Stranger");

        var error = await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(s.Db, NewTask(s, s.Anna.Id, stranger.Id)));

        Assert.Contains("member", error.Message);
    }

    [Fact]
    public async Task Create_TheSamePersonTwice_IsRefused()
    {
        var s = await SetUpAsync();

        await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id, s.Ben.Id)));
    }

    [Fact]
    public async Task Create_MoreThanTenPeople_IsRefused()
    {
        var s = await SetUpAsync();
        var crowd = new List<Guid>();
        for (var i = 0; i < ItemParticipant.MaxPerItem + 1; i++)
        {
            var user = await TestDb.AddUserAsync(s.Db, $"U{i}");
            s.Db.TeamMembers.Add(new TeamMember { TeamId = s.Team.Id, UserId = user.Id, Role = TeamRole.Member });
            crowd.Add(user.Id);
        }
        await s.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(s.Db, NewTask(s, s.Anna.Id, crowd.ToArray())));
        var ten = crowd.Take(ItemParticipant.MaxPerItem).ToArray();
        Assert.Equal(10, (await CreateAsync(s.Db, NewTask(s, s.Anna.Id, ten))).ParticipantUserIds!.Count);
    }

    [Fact]
    public async Task Create_InAPersonalWorkspace_IsRefused()
    {
        var db = TestDb.Create();
        var me = await TestDb.AddUserAsync(db, "Me");
        var friend = await TestDb.AddUserAsync(db, "Friend");
        var personal = new Team { Name = "Personal", IsPersonal = true };
        db.Teams.Add(personal);
        db.TeamMembers.Add(new TeamMember { TeamId = personal.Id, UserId = me.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(db,
            new CreateTaskCommand(personal.Id, "Solo", null, me.Id, NextWeek, 5, null, me.Id, ParticipantUserIds: [friend.Id])));

        Assert.Contains("personal", error.Message);
    }

    [Theory]
    [InlineData(EventTemplate.OutOfOffice)]
    [InlineData(EventTemplate.FocusTime)]
    [InlineData(EventTemplate.WorkingLocation)]
    [InlineData(EventTemplate.Birthday)]
    public async Task Create_AKindWithoutGuests_KeepsNone_NotAnError(EventTemplate template)
    {
        var s = await SetUpAsync();

        var dto = await CreateAsync(s.Db, NewEvent(s, s.Anna.Id, template, s.Ben.Id));

        Assert.Empty(dto.ParticipantUserIds!);
        Assert.Empty(await ParticipantsOf(s.Db, dto.Id));
    }

    [Fact]
    public async Task Create_TellsTheNewParticipants_OnceEach_AndTheAssigneeSeparately()
    {
        var s = await SetUpAsync();

        await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id, s.Cora.Id));

        var added = s.Db.EmailOutbox.Where(m => m.Event == NotificationEvent.ParticipantAdded).ToList();
        Assert.Equal(2, added.Count);
        Assert.Contains(added, m => m.ToEmail == s.Ben.Email && m.TextBody.Contains("co-contractor") && m.TextBody.Contains("Olga"));
        Assert.Single(s.Db.EmailOutbox, m => m.Event == NotificationEvent.TaskAssignedToYou && m.ToEmail == s.Anna.Email);
        Assert.Equal(2, s.Db.Notifications.Count(n => n.Event == NotificationEvent.ParticipantAdded)); // inbox + push too
    }

    [Fact]
    public async Task Create_AParticipantDoesNotAlsoGetTheTeamAnnouncement()
    {
        var s = await SetUpAsync();
        s.Db.NotificationPreferences.Add(new NotificationPreference { UserId = s.Ben.Id, TeamTasks = true });
        s.Db.NotificationPreferences.Add(new NotificationPreference { UserId = s.Cora.Id, TeamTasks = true });
        await s.Db.SaveChangesAsync();

        await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id));

        Assert.Single(s.Db.EmailOutbox, m => m.Event == NotificationEvent.TeamTaskAdded && m.ToEmail == s.Cora.Email);
        Assert.DoesNotContain(s.Db.EmailOutbox, m => m.Event == NotificationEvent.TeamTaskAdded && m.ToEmail == s.Ben.Email);
    }

    [Fact]
    public async Task Create_RespectsAParticipantsOwnSwitch()
    {
        var s = await SetUpAsync();
        s.Db.NotificationPreferences.Add(new NotificationPreference { UserId = s.Ben.Id, TaskAssignments = false });
        await s.Db.SaveChangesAsync();

        await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id));

        Assert.DoesNotContain(s.Db.EmailOutbox, m => m.ToEmail == s.Ben.Email);
        Assert.DoesNotContain(s.Db.Notifications, n => n.UserId == s.Ben.Id);
    }

    // ---- update ----

    [Fact]
    public async Task Update_NoList_KeepsTheParticipants()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id));
        var item = await s.Db.CalendarItems.SingleAsync(i => i.Id == dto.Id);

        var updated = await UpdateAsync(s.Db, item, s.Anna.Id, null);

        Assert.Equal([s.Ben.Id], updated.ParticipantUserIds);
        Assert.Single(await ParticipantsOf(s.Db, dto.Id));
    }

    [Fact]
    public async Task Update_AddAndRemove_TellsOnlyThoseWhoseSituationChanged()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id));
        var item = await s.Db.CalendarItems.SingleAsync(i => i.Id == dto.Id);
        ClearNotifications(s.Db);

        // Ben is dropped, Cora joins.
        await UpdateAsync(s.Db, item, s.Anna.Id, [s.Cora.Id]);

        Assert.Equal([s.Cora.Id], (await ParticipantsOf(s.Db, dto.Id)).Select(p => p.UserId));
        Assert.Single(s.Db.EmailOutbox, m => m.Event == NotificationEvent.ParticipantAdded && m.ToEmail == s.Cora.Email);
        var removed = Assert.Single(s.Db.EmailOutbox, m => m.Event == NotificationEvent.ParticipantRemoved);
        Assert.Equal(s.Ben.Email, removed.ToEmail);
    }

    [Fact]
    public async Task Update_AParticipantWhoStays_HearsAboutAnEdit_AsDoesTheAssignee_ButNotTheActor()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id, s.Cora.Id));
        var item = await s.Db.CalendarItems.SingleAsync(i => i.Id == dto.Id);
        ClearNotifications(s.Db);

        // The owner renames the task and adds nobody: Anna (assignee) and both co-contractors are told.
        await UpdateAsync(s.Db, item, s.Owner.Id, [s.Ben.Id, s.Cora.Id], title: "Report v2");

        var changed = s.Db.EmailOutbox.Where(m => m.Event == NotificationEvent.TaskChanged).Select(m => m.ToEmail).ToList();
        Assert.Equal(3, changed.Count);
        Assert.Contains(s.Anna.Email, changed);
        Assert.Contains(s.Ben.Email, changed);
        Assert.Contains(s.Cora.Email, changed);
        Assert.DoesNotContain(s.Owner.Email, changed);
    }

    [Fact]
    public async Task Update_ANewParticipant_GetsTheInvitation_NotAlsoTheChangeMail()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id));
        var item = await s.Db.CalendarItems.SingleAsync(i => i.Id == dto.Id);
        ClearNotifications(s.Db);

        await UpdateAsync(s.Db, item, s.Owner.Id, [s.Ben.Id], title: "Report v2");

        Assert.DoesNotContain(s.Db.EmailOutbox, m => m.Event == NotificationEvent.TaskChanged && m.ToEmail == s.Ben.Email);
        Assert.Single(s.Db.EmailOutbox, m => m.Event == NotificationEvent.ParticipantAdded && m.ToEmail == s.Ben.Email);
    }

    [Fact]
    public async Task Update_HandingTheTaskToAParticipant_MakesThemTheAssignee_NotARemovedParticipant()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id));
        var item = await s.Db.CalendarItems.SingleAsync(i => i.Id == dto.Id);
        ClearNotifications(s.Db);

        var updated = await UpdateAsync(s.Db, item, s.Owner.Id, null, assignee: s.Ben.Id, keepAssignee: false);

        Assert.Equal(s.Ben.Id, updated.AssignedUserId);
        Assert.Empty(updated.ParticipantUserIds!);
        Assert.Empty(await ParticipantsOf(s.Db, dto.Id));
        Assert.Single(s.Db.EmailOutbox, m => m.Event == NotificationEvent.TaskAssignedToYou && m.ToEmail == s.Ben.Email);
        Assert.DoesNotContain(s.Db.EmailOutbox, m => m.Event == NotificationEvent.ParticipantRemoved);
    }

    [Fact]
    public async Task Update_TurningAnEventIntoATask_TurnsGuestsIntoCoContractors()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewEvent(s, s.Anna.Id, participants: [s.Ben.Id]));
        var item = await s.Db.CalendarItems.SingleAsync(i => i.Id == dto.Id);

        await new UpdateTaskCommandHandler(s.Db, CacheMock().Object, new NotificationService(s.Db)).Handle(
            new UpdateTaskCommand(item.Id, item.Title, null, TaskStatus.Backlog, s.Anna.Id, NextWeek.AddDays(2), 5, null, null, null, s.Owner.Id,
                TaskType.Task, null, null, ParticipantUserIds: [s.Ben.Id]),
            CancellationToken.None);

        Assert.Equal(ParticipantRole.CoContractor, Assert.Single(await ParticipantsOf(s.Db, dto.Id)).Role);
    }

    // ---- the list query ----

    [Fact]
    public async Task TeamList_CarriesEachItemsParticipants()
    {
        var s = await SetUpAsync();
        var withPeople = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id, s.Cora.Id));
        var alone = await CreateAsync(s.Db, NewTask(s, s.Anna.Id));

        var list = await new GetTasksByTeamQueryHandler(s.Db, CacheMock().Object).Handle(new GetTasksByTeamQuery(s.Team.Id), CancellationToken.None);

        Assert.Equal(2, list.Single(t => t.Id == withPeople.Id).ParticipantUserIds!.Count);
        Assert.Empty(list.Single(t => t.Id == alone.Id).ParticipantUserIds!);
    }

    // ---- delete / leave / leaving the team ----

    [Fact]
    public async Task Delete_TellsTheParticipantsAsWellAsTheAssignee()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id));
        ClearNotifications(s.Db);

        await new DeleteTaskCommandHandler(s.Db, CacheMock().Object, new NotificationService(s.Db))
            .Handle(new DeleteTaskCommand(dto.Id, s.Owner.Id), CancellationToken.None);

        var told = s.Db.EmailOutbox.Where(m => m.Event == NotificationEvent.TaskDeleted).Select(m => m.ToEmail).ToList();
        Assert.Equal(2, told.Count);
        Assert.Contains(s.Anna.Email, told);
        Assert.Contains(s.Ben.Email, told);
    }

    [Fact]
    public async Task Leave_EndsTheCallersOwnParticipation_TellsTheAssignee_AndDropsTheCachedList()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id, s.Cora.Id));
        ClearNotifications(s.Db);
        var cache = CacheMock();

        await new LeaveTaskCommandHandler(s.Db, cache.Object, new NotificationService(s.Db))
            .Handle(new LeaveTaskCommand(dto.Id, s.Ben.Id), CancellationToken.None);

        Assert.Equal([s.Cora.Id], (await ParticipantsOf(s.Db, dto.Id)).Select(p => p.UserId));
        var mail = Assert.Single(s.Db.EmailOutbox);
        Assert.Equal(s.Anna.Email, mail.ToEmail);
        Assert.Contains("Ben is no longer a co-contractor", mail.TextBody);
        cache.Verify(c => c.RemoveAsync(CacheKeys.TeamTasks(s.Team.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Leave_SomeoneWhoDoesNotTakePart_IsNotFound()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new LeaveTaskCommandHandler(s.Db, CacheMock().Object, new NotificationService(s.Db))
                .Handle(new LeaveTaskCommand(dto.Id, s.Cora.Id), CancellationToken.None));
    }

    [Fact]
    public async Task RemovingAMemberFromTheTeam_EndsTheirParticipationToo()
    {
        var s = await SetUpAsync();
        var dto = await CreateAsync(s.Db, NewTask(s, s.Anna.Id, s.Ben.Id, s.Cora.Id));

        await new RemoveTeamMemberCommandHandler(s.Db, CacheMock().Object, new NotificationService(s.Db))
            .Handle(new RemoveTeamMemberCommand(s.Team.Id, s.Owner.Id, s.Ben.Id), CancellationToken.None);

        Assert.Equal([s.Cora.Id], (await ParticipantsOf(s.Db, dto.Id)).Select(p => p.UserId));
    }
}
