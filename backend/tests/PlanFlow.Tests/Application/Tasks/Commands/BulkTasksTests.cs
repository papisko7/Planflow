using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.BulkTasks;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

/// <summary>
/// Selection mode and "Clear" act on many items in one request. The permission is checked per item (Owner/Admin: any item, a
/// Member: only their own), what could not be changed is reported instead of failing the batch, and the team is rescored once.
/// </summary>
public class BulkTasksTests
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

    private static CalendarItem AddTask(Setup s, Guid? assignee, string title = "Report", TaskStatus status = TaskStatus.Backlog, Guid? blockedBy = null)
    {
        var item = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = assignee, Title = title, TaskType = TaskType.Task, Status = status,
            DeadlineUtc = NextWeek, ImpactScore = 5, BlockedByTaskId = blockedBy
        };
        s.Db.CalendarItems.Add(item);
        s.Db.SaveChanges();
        return item;
    }

    private static CalendarItem AddEvent(Setup s, Guid? assignee, string title = "Retro")
    {
        var item = new CalendarItem
        {
            TeamId = s.Team.Id, AssignedUserId = assignee, Title = title, TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            StartUtc = NextWeek.AddHours(9), EndUtc = NextWeek.AddHours(10), ImpactScore = 5
        };
        s.Db.CalendarItems.Add(item);
        s.Db.SaveChanges();
        return item;
    }

    private static Task<BulkTaskResult> Delete(Setup s, User actor, bool any, params Guid[] ids) =>
        new BulkDeleteTasksCommandHandler(s.Db, new Mock<ICacheService>().Object, new NotificationService(s.Db))
            .Handle(new BulkDeleteTasksCommand(s.Team.Id, ids, actor.Id, any), CancellationToken.None);

    private static Task<BulkTaskResult> SetStatus(Setup s, User actor, bool any, TaskStatus status, params Guid[] ids) =>
        new BulkSetStatusCommandHandler(s.Db, new Mock<ICacheService>().Object, new NotificationService(s.Db))
            .Handle(new BulkSetStatusCommand(s.Team.Id, ids, status, actor.Id, any), CancellationToken.None);

    private static Task<BulkTaskResult> Move(Setup s, User actor, bool any, Guid? listId, params Guid[] ids) =>
        new BulkMoveToListCommandHandler(s.Db, new Mock<ICacheService>().Object)
            .Handle(new BulkMoveToListCommand(s.Team.Id, ids, listId, actor.Id, any), CancellationToken.None);

    // ---- delete ----

    [Fact]
    public async Task Delete_OwnerRemovesAnyItemsOfTheTeam()
    {
        var s = await SetUpAsync();
        var a = AddTask(s, s.Anna.Id);
        var b = AddEvent(s, s.Ben.Id);
        var keep = AddTask(s, s.Ben.Id, "Keep");

        var result = await Delete(s, s.Owner, true, a.Id, b.Id);

        Assert.Equal(2, result.Succeeded);
        Assert.Empty(result.SkippedIds);
        Assert.Equal(keep.Id, (await s.Db.CalendarItems.SingleAsync()).Id);
    }

    [Fact]
    public async Task Delete_AMemberRemovesOnlyTheirOwn_TheRestIsReportedAsSkipped()
    {
        var s = await SetUpAsync();
        var mine = AddTask(s, s.Anna.Id);
        var theirs = AddTask(s, s.Ben.Id, "Ben's");
        var unassigned = AddTask(s, null, "Nobody's");

        var result = await Delete(s, s.Anna, false, mine.Id, theirs.Id, unassigned.Id);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal([theirs.Id, unassigned.Id], result.SkippedIds);
        Assert.Equal(2, await s.Db.CalendarItems.CountAsync());
    }

    [Fact]
    public async Task Delete_AnIdOfAnotherTeamOrNoItemAtAll_IsSkipped_NeverDeleted()
    {
        var s = await SetUpAsync();
        var otherTeam = await TestDb.AddTeamAsync(s.Db, (s.Owner, TeamRole.Owner));
        var foreign = new CalendarItem { TeamId = otherTeam.Id, Title = "Elsewhere", TaskType = TaskType.Task, DeadlineUtc = NextWeek };
        s.Db.CalendarItems.Add(foreign);
        await s.Db.SaveChangesAsync();
        var ghost = Guid.NewGuid();

        var result = await Delete(s, s.Owner, true, foreign.Id, ghost);

        Assert.Equal(0, result.Succeeded);
        Assert.Contains(foreign.Id, result.SkippedIds);
        Assert.Contains(ghost, result.SkippedIds);
        Assert.Single(s.Db.CalendarItems);
    }

    [Fact]
    public async Task Delete_FreesTheItemsTheRemovedOnesBlocked_AndRescoresTheTeamOnce()
    {
        var s = await SetUpAsync();
        var blocker = AddTask(s, s.Anna.Id, "Blocker");
        var waiting = AddTask(s, s.Anna.Id, "Waiting", blockedBy: blocker.Id);

        await Delete(s, s.Anna, false, blocker.Id);

        Assert.Null((await s.Db.CalendarItems.SingleAsync(t => t.Id == waiting.Id)).BlockedByTaskId);
        Assert.True((await s.Db.CalendarItems.SingleAsync(t => t.Id == waiting.Id)).CurrentUrgencyScore > 0);
    }

    [Fact]
    public async Task Delete_TellsTheAssigneeWhenSomeoneElseRemovesTheirItems_ButNotWhenTheyRemoveTheirOwn()
    {
        var s = await SetUpAsync();
        var annas = AddTask(s, s.Anna.Id, "Anna's");
        var bens = AddTask(s, s.Ben.Id, "Ben's");

        await Delete(s, s.Owner, true, annas.Id);
        var told = s.Db.EmailOutbox.Where(m => m.Event == NotificationEvent.TaskDeleted).Select(m => m.ToEmail).ToList();
        Assert.Equal([s.Anna.Email], told);

        s.Db.EmailOutbox.RemoveRange(s.Db.EmailOutbox.ToList());
        await s.Db.SaveChangesAsync();
        await Delete(s, s.Ben, false, bens.Id);
        Assert.Empty(s.Db.EmailOutbox);
    }

    [Fact]
    public async Task Delete_TheParticipantsGoWithTheItem()
    {
        var s = await SetUpAsync();
        var item = AddEvent(s, s.Anna.Id);
        s.Db.ItemParticipants.Add(new ItemParticipant { CalendarItemId = item.Id, UserId = s.Ben.Id, Role = ParticipantRole.Guest });
        await s.Db.SaveChangesAsync();

        await Delete(s, s.Anna, false, item.Id);

        Assert.Empty(s.Db.ItemParticipants);
        Assert.Equal([s.Ben.Email], s.Db.EmailOutbox.Where(m => m.Event == NotificationEvent.TaskDeleted).Select(m => m.ToEmail).ToList());
    }

    [Fact]
    public async Task Delete_NoIds_IsRejectedByTheValidator()
    {
        var validator = new BulkDeleteTasksCommandValidator();

        Assert.False((await validator.ValidateAsync(new BulkDeleteTasksCommand(Guid.NewGuid(), [], Guid.NewGuid(), true))).IsValid);
        var tooMany = Enumerable.Range(0, BulkTaskLimits.MaxIds + 1).Select(_ => Guid.NewGuid()).ToList();
        Assert.False((await validator.ValidateAsync(new BulkDeleteTasksCommand(Guid.NewGuid(), tooMany, Guid.NewGuid(), true))).IsValid);
    }

    // ---- status ----

    [Fact]
    public async Task Status_SetsItOnEveryItemAndLeavesAHistoryEntryEach()
    {
        var s = await SetUpAsync();
        var a = AddTask(s, s.Anna.Id);
        var b = AddTask(s, s.Anna.Id, "Second");

        var result = await SetStatus(s, s.Anna, false, TaskStatus.Completed, a.Id, b.Id);

        Assert.Equal(2, result.Succeeded);
        Assert.All(await s.Db.CalendarItems.ToListAsync(), t => Assert.Equal(TaskStatus.Completed, t.Status));
        Assert.Equal(2, await s.Db.TaskHistories.CountAsync(h => h.ChangeType == TaskChangeType.StatusChanged));
    }

    [Fact]
    public async Task Status_AStatusTheItemsTypeDoesNotHave_IsSkipped_NotConverted()
    {
        var s = await SetUpAsync();
        var task = AddTask(s, s.Anna.Id);
        var ev = AddEvent(s, s.Anna.Id);

        var result = await SetStatus(s, s.Anna, false, TaskStatus.Completed, task.Id, ev.Id);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal([ev.Id], result.SkippedIds);
        Assert.Equal(TaskStatus.Confirmed, (await s.Db.CalendarItems.SingleAsync(t => t.Id == ev.Id)).Status);
    }

    [Fact]
    public async Task Status_CancelledFitsBothTypes()
    {
        var s = await SetUpAsync();
        var task = AddTask(s, s.Anna.Id);
        var ev = AddEvent(s, s.Anna.Id);

        var result = await SetStatus(s, s.Anna, false, TaskStatus.Cancelled, task.Id, ev.Id);

        Assert.Equal(2, result.Succeeded);
        Assert.All(await s.Db.CalendarItems.ToListAsync(), t => Assert.Equal(TaskStatus.Cancelled, t.Status));
    }

    [Fact]
    public async Task Status_AMemberCannotChangeSomeoneElsesItem()
    {
        var s = await SetUpAsync();
        var theirs = AddTask(s, s.Ben.Id);

        var result = await SetStatus(s, s.Anna, false, TaskStatus.Completed, theirs.Id);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal([theirs.Id], result.SkippedIds);
        Assert.Equal(TaskStatus.Backlog, (await s.Db.CalendarItems.SingleAsync()).Status);
    }

    [Fact]
    public async Task Status_FinishingAnItemFreesWhatItBlocked_ByRescoringTheTeam()
    {
        var s = await SetUpAsync();
        var blocker = AddTask(s, s.Anna.Id, "Blocker");
        var waiting = AddTask(s, s.Anna.Id, "Waiting", blockedBy: blocker.Id);
        // Scores as they stand before the bulk action (the blocked item is halved).
        await new BulkSetStatusCommandHandler(s.Db, new Mock<ICacheService>().Object, new NotificationService(s.Db))
            .Handle(new BulkSetStatusCommand(s.Team.Id, [waiting.Id], TaskStatus.InProgress, s.Anna.Id, false), CancellationToken.None);
        var halved = (await s.Db.CalendarItems.SingleAsync(t => t.Id == waiting.Id)).CurrentUrgencyScore;

        await SetStatus(s, s.Anna, false, TaskStatus.Completed, blocker.Id);

        Assert.True((await s.Db.CalendarItems.SingleAsync(t => t.Id == waiting.Id)).CurrentUrgencyScore > halved);
    }

    [Fact]
    public async Task Status_TellsTheAssigneeWhenAnAdminChangesIt_ButNotForAChangeToTheirOwnItem()
    {
        var s = await SetUpAsync();
        var annas = AddTask(s, s.Anna.Id);

        await SetStatus(s, s.Owner, true, TaskStatus.InProgress, annas.Id);
        Assert.Equal([s.Anna.Email], s.Db.EmailOutbox.Where(m => m.Event == NotificationEvent.TaskChanged).Select(m => m.ToEmail).ToList());

        s.Db.EmailOutbox.RemoveRange(s.Db.EmailOutbox.ToList());
        await s.Db.SaveChangesAsync();
        await SetStatus(s, s.Anna, false, TaskStatus.Backlog, annas.Id);
        Assert.Empty(s.Db.EmailOutbox);
    }

    // ---- move to a list ----

    private static async Task<ItemList> AddList(Setup s, ItemListKind kind, string name = "Sprint")
    {
        var list = new ItemList { TeamId = s.Team.Id, Name = name, Kind = kind, CreatedByUserId = s.Owner.Id };
        s.Db.ItemLists.Add(list);
        await s.Db.SaveChangesAsync();
        return list;
    }

    [Fact]
    public async Task Move_TasksGoToATaskList_AndEventsStayWhereTheyAre()
    {
        var s = await SetUpAsync();
        var list = await AddList(s, ItemListKind.TaskList);
        var task = AddTask(s, s.Anna.Id);
        var ev = AddEvent(s, s.Anna.Id);

        var result = await Move(s, s.Anna, false, list.Id, task.Id, ev.Id);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal([ev.Id], result.SkippedIds);
        Assert.Equal(list.Id, (await s.Db.CalendarItems.SingleAsync(t => t.Id == task.Id)).ItemListId);
        Assert.Null((await s.Db.CalendarItems.SingleAsync(t => t.Id == ev.Id)).ItemListId);
    }

    [Fact]
    public async Task Move_ANullListTakesTheItemsOutOfTheirList()
    {
        var s = await SetUpAsync();
        var list = await AddList(s, ItemListKind.MeetingList);
        var ev = AddEvent(s, s.Anna.Id);
        ev.ItemListId = list.Id;
        await s.Db.SaveChangesAsync();

        var result = await Move(s, s.Anna, false, null, ev.Id);

        Assert.Equal(1, result.Succeeded);
        Assert.Null((await s.Db.CalendarItems.SingleAsync()).ItemListId);
    }

    [Fact]
    public async Task Move_AListOfAnotherTeam_IsRejected()
    {
        var s = await SetUpAsync();
        var otherTeam = await TestDb.AddTeamAsync(s.Db, (s.Owner, TeamRole.Owner));
        var foreign = new ItemList { TeamId = otherTeam.Id, Name = "Elsewhere", Kind = ItemListKind.TaskList };
        s.Db.ItemLists.Add(foreign);
        await s.Db.SaveChangesAsync();
        var task = AddTask(s, s.Anna.Id);

        await Assert.ThrowsAsync<ValidationException>(() => Move(s, s.Anna, false, foreign.Id, task.Id));
    }

    [Fact]
    public async Task Move_AMemberMovesOnlyTheirOwnItems()
    {
        var s = await SetUpAsync();
        var list = await AddList(s, ItemListKind.TaskList);
        var mine = AddTask(s, s.Anna.Id);
        var theirs = AddTask(s, s.Ben.Id);

        var result = await Move(s, s.Anna, false, list.Id, mine.Id, theirs.Id);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal([theirs.Id], result.SkippedIds);
    }
}
