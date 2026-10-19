using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.ItemLists;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.ItemLists;

/// <summary>
/// The team's own lists of tasks and of meetings. Anyone in the team makes one, its creator and Owner/Admin change or delete it,
/// and deleting a list never deletes what is in it.
/// </summary>
public class ItemListsTests
{
    private static readonly DateTime NextWeek = DateTime.UtcNow.Date.AddDays(7);

    private sealed record Setup(PlanFlowDbContext Db, Team Team, User Owner, User Anna, User Ben);

    private static async Task<Setup> SetUpAsync()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db, "Olga");
        var anna = await TestDb.AddUserAsync(db, "Anna");
        var ben = await TestDb.AddUserAsync(db, "Ben");
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner), (anna, TeamRole.Member), (ben, TeamRole.Member));
        return new Setup(db, team, owner, anna, ben);
    }

    private static Task<ItemListDto> Create(Setup s, User by, string name, ItemListKind kind = ItemListKind.TaskList) =>
        new CreateItemListCommandHandler(s.Db).Handle(new CreateItemListCommand(s.Team.Id, name, kind, by.Id), CancellationToken.None);

    private static Task<ItemListDto> Rename(Setup s, Guid listId, string name, User by, bool any) =>
        new RenameItemListCommandHandler(s.Db).Handle(new RenameItemListCommand(s.Team.Id, listId, name, by.Id, any), CancellationToken.None);

    private static Task Delete(Setup s, Guid listId, User by, bool any) =>
        new DeleteItemListCommandHandler(s.Db, new Mock<ICacheService>().Object)
            .Handle(new DeleteItemListCommand(s.Team.Id, listId, by.Id, any), CancellationToken.None);

    private static Task<TaskDto> CreateItem(Setup s, TaskType type, Guid? listId) =>
        new CreateTaskCommandHandler(s.Db, new Mock<ICacheService>().Object, new NotificationService(s.Db)).Handle(
            type == TaskType.Event
                ? new CreateTaskCommand(s.Team.Id, "Retro", null, s.Anna.Id, null, 5, null, s.Anna.Id, TaskType.Event,
                    StartUtc: NextWeek.AddHours(9), EndUtc: NextWeek.AddHours(10), ItemListId: listId)
                : new CreateTaskCommand(s.Team.Id, "Report", null, s.Anna.Id, NextWeek, 5, null, s.Anna.Id, ItemListId: listId),
            CancellationToken.None);

    // ---- create / read ----

    [Fact]
    public async Task Create_AnyMemberMakesAListOfEitherKind_AndTheNameIsTrimmed()
    {
        var s = await SetUpAsync();

        var tasks = await Create(s, s.Anna, "  Sprint 12  ");
        var meetings = await Create(s, s.Anna, "Standups", ItemListKind.MeetingList);

        Assert.Equal("Sprint 12", tasks.Name);
        Assert.Equal(ItemListKind.TaskList, tasks.Kind);
        Assert.Equal(s.Anna.Id, tasks.CreatedByUserId);
        Assert.Equal(ItemListKind.MeetingList, meetings.Kind);
    }

    [Fact]
    public async Task Create_TheSameNameTwiceInOneKind_IsAConflict_ButAnotherKindMayReuseIt()
    {
        var s = await SetUpAsync();
        await Create(s, s.Anna, "Team");

        await Assert.ThrowsAsync<ConflictException>(() => Create(s, s.Ben, "TEAM"));
        var meeting = await Create(s, s.Ben, "Team", ItemListKind.MeetingList);

        Assert.Equal(ItemListKind.MeetingList, meeting.Kind);
    }

    [Fact]
    public async Task Create_AnEmptyOrTooLongName_IsRejectedByTheValidator()
    {
        var validator = new CreateItemListCommandValidator();

        Assert.False((await validator.ValidateAsync(new CreateItemListCommand(Guid.NewGuid(), "  ", ItemListKind.TaskList, Guid.NewGuid()))).IsValid);
        Assert.False((await validator.ValidateAsync(new CreateItemListCommand(Guid.NewGuid(), new string('x', ItemList.MaxNameLength + 1), ItemListKind.TaskList, Guid.NewGuid()))).IsValid);
        Assert.True((await validator.ValidateAsync(new CreateItemListCommand(Guid.NewGuid(), new string('x', ItemList.MaxNameLength), ItemListKind.TaskList, Guid.NewGuid()))).IsValid);
    }

    [Fact]
    public async Task Get_ListsOfTheTeamByName_NeverOfAnotherTeam()
    {
        var s = await SetUpAsync();
        await Create(s, s.Anna, "Zeta");
        await Create(s, s.Anna, "Alpha");
        var other = await TestDb.AddTeamAsync(s.Db, (s.Owner, TeamRole.Owner));
        s.Db.ItemLists.Add(new ItemList { TeamId = other.Id, Name = "Hidden", Kind = ItemListKind.TaskList });
        await s.Db.SaveChangesAsync();

        var lists = await new GetItemListsQueryHandler(s.Db).Handle(new GetItemListsQuery(s.Team.Id), CancellationToken.None);

        Assert.Equal(["Alpha", "Zeta"], lists.Select(l => l.Name));
    }

    // ---- rename / delete ----

    [Fact]
    public async Task Rename_TheCreatorAndAnAdminMay_AnotherMemberMayNot()
    {
        var s = await SetUpAsync();
        var list = await Create(s, s.Anna, "Sprint");

        Assert.Equal("Sprint 2", (await Rename(s, list.Id, "Sprint 2", s.Anna, false)).Name);
        Assert.Equal("Sprint 3", (await Rename(s, list.Id, "Sprint 3", s.Owner, true)).Name);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Rename(s, list.Id, "Mine now", s.Ben, false));
    }

    [Fact]
    public async Task Rename_ToAnExistingNameOfTheSameKind_IsAConflict_ButKeepingItsOwnNameIsFine()
    {
        var s = await SetUpAsync();
        await Create(s, s.Anna, "One");
        var two = await Create(s, s.Anna, "Two");

        await Assert.ThrowsAsync<ConflictException>(() => Rename(s, two.Id, "one", s.Anna, false));
        Assert.Equal("TWO", (await Rename(s, two.Id, "TWO", s.Anna, false)).Name);
    }

    [Fact]
    public async Task Rename_AListOfAnotherTeam_IsNotFound()
    {
        var s = await SetUpAsync();
        var other = await TestDb.AddTeamAsync(s.Db, (s.Owner, TeamRole.Owner));
        var foreign = new ItemList { TeamId = other.Id, Name = "Elsewhere", Kind = ItemListKind.TaskList, CreatedByUserId = s.Owner.Id };
        s.Db.ItemLists.Add(foreign);
        await s.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => Rename(s, foreign.Id, "Mine", s.Owner, true));
    }

    [Fact]
    public async Task Delete_KeepsTheItemsAndOnlyClearsTheirList()
    {
        var s = await SetUpAsync();
        var list = await Create(s, s.Anna, "Sprint");
        var item = await CreateItem(s, TaskType.Task, list.Id);
        Assert.Equal(list.Id, item.ItemListId);

        await Delete(s, list.Id, s.Anna, false);

        Assert.Empty(s.Db.ItemLists);
        var kept = await s.Db.CalendarItems.SingleAsync();
        Assert.Equal(item.Id, kept.Id);
        Assert.Null(kept.ItemListId);
    }

    [Fact]
    public async Task Delete_OnlyTheCreatorOrAnAdmin()
    {
        var s = await SetUpAsync();
        var list = await Create(s, s.Anna, "Sprint");

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Delete(s, list.Id, s.Ben, false));
        await Delete(s, list.Id, s.Owner, true);

        Assert.Empty(s.Db.ItemLists);
    }

    // ---- items in lists ----

    [Fact]
    public async Task CreateItem_InAListOfItsKind_StoresIt_AndTheListOfTheOtherKindIsDropped()
    {
        var s = await SetUpAsync();
        var taskList = await Create(s, s.Anna, "Sprint");
        var meetingList = await Create(s, s.Anna, "Standups", ItemListKind.MeetingList);

        Assert.Equal(taskList.Id, (await CreateItem(s, TaskType.Task, taskList.Id)).ItemListId);
        Assert.Equal(meetingList.Id, (await CreateItem(s, TaskType.Event, meetingList.Id)).ItemListId);
        Assert.Null((await CreateItem(s, TaskType.Event, taskList.Id)).ItemListId); // an event does not fit a task list
        Assert.Null((await CreateItem(s, TaskType.Task, null)).ItemListId);
    }

    [Fact]
    public async Task CreateItem_InAListOfAnotherTeam_IsRejected()
    {
        var s = await SetUpAsync();
        var other = await TestDb.AddTeamAsync(s.Db, (s.Owner, TeamRole.Owner));
        var foreign = new ItemList { TeamId = other.Id, Name = "Elsewhere", Kind = ItemListKind.TaskList };
        s.Db.ItemLists.Add(foreign);
        await s.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => CreateItem(s, TaskType.Task, foreign.Id));
    }

    [Fact]
    public async Task UpdateItem_ChangesOrClearsTheList_AndTurningATaskIntoAnEventLeavesTheTaskList()
    {
        var s = await SetUpAsync();
        var taskList = await Create(s, s.Anna, "Sprint");
        var item = await CreateItem(s, TaskType.Task, null);
        var handler = new UpdateTaskCommandHandler(s.Db, new Mock<ICacheService>().Object, new NotificationService(s.Db));

        UpdateTaskCommand Edit(TaskType type, Guid? listId) => new(
            item.Id, "Report", null, type == TaskType.Event ? TaskStatus.Confirmed : TaskStatus.Backlog, s.Anna.Id,
            type == TaskType.Event ? null : NextWeek, 5, null, null, null, s.Anna.Id, type,
            StartUtc: type == TaskType.Event ? NextWeek.AddHours(9) : null, EndUtc: type == TaskType.Event ? NextWeek.AddHours(10) : null,
            ItemListId: listId);

        Assert.Equal(taskList.Id, (await handler.Handle(Edit(TaskType.Task, taskList.Id), CancellationToken.None)).ItemListId);
        Assert.Null((await handler.Handle(Edit(TaskType.Task, null), CancellationToken.None)).ItemListId);
        await handler.Handle(Edit(TaskType.Task, taskList.Id), CancellationToken.None);
        Assert.Null((await handler.Handle(Edit(TaskType.Event, taskList.Id), CancellationToken.None)).ItemListId);
    }
}
