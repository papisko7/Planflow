using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Application.Tasks.Queries.GetTaskDetail;
using PlanFlow.Application.Tasks.Queries.GetTasksByTeam;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

/// <summary>
/// A task imported from Google Tasks. Every field can be edited in the app (the sync brings Google's title, notes and due day back only when
/// the task changes in Google). A task with no due day in Google carries a placeholder deadline that the user replaces or confirms by saving.
/// </summary>
public class GoogleTaskEditTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    private static DateTime Day(int daysFromToday) => DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(daysFromToday), DateTimeKind.Utc);

    private static async Task<(PlanFlowDbContext Db, CalendarItem Item, Guid UserId)> SetUpAsync(bool defaultedDeadline = false)
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        var list = new ExternalTaskList { CalendarIntegrationId = Guid.NewGuid(), ExternalId = "l1", Title = "Shopping" };
        var item = new CalendarItem
        {
            TeamId = team.Id, AssignedUserId = owner.Id, Title = "Google title", Description = "Google notes", TaskType = TaskType.Task,
            Status = TaskStatus.Backlog, IsAllDay = true, StartUtc = Day(0), DeadlineUtc = Day(5), IsDeadlineDefaulted = defaultedDeadline,
            ExternalTaskId = "t1", ExternalTaskList = list, IsDataIncomplete = true
        };
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();
        return (db, item, owner.Id);
    }

    private static Task<TaskDto> UpdateAsync(
        PlanFlowDbContext db, CalendarItem item, Guid userId, DateTime? start, DateTime? deadline, double? effort = null, int? impact = 7,
        string title = "My title", TaskStatus status = TaskStatus.Backlog) =>
        new UpdateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new UpdateTaskCommand(item.Id, title, "My notes", status, userId, deadline, impact, null, null, null, userId, TaskType.Task,
                StartUtc: start, EstimatedEffortHours: effort, IsAllDay: true),
            CancellationToken.None);

    [Fact]
    public async Task Update_TakesEveryField_TheTitleNotesDeadlineStartEffortImpactAndStatus()
    {
        var (db, item, userId) = await SetUpAsync();

        var dto = await UpdateAsync(db, item, userId, start: Day(2), deadline: Day(9), effort: 4, impact: 8, status: TaskStatus.InProgress);

        Assert.Equal("My title", dto.Title);                   // editable in the app although Google has it too
        Assert.Equal("My notes", dto.Description);
        Assert.Equal(Day(9), dto.DeadlineUtc);
        Assert.Equal(Day(2), dto.StartUtc);
        Assert.Equal(4, dto.EstimatedEffortHours);
        Assert.Equal(8, dto.ImpactScore);
        Assert.Equal(TaskStatus.InProgress, dto.Status);
        Assert.False(dto.IsDataIncomplete);                    // saving confirms the item
        Assert.True(dto.IsExternal);
        Assert.True(dto.IsAllDay);
    }

    [Fact]
    public async Task Update_StartIsKeptAsAWholeDay_EvenIfTheRequestCarriesATimeOfDay()
    {
        var (db, item, userId) = await SetUpAsync();

        var dto = await UpdateAsync(db, item, userId, start: Day(3).AddHours(14), deadline: Day(5));

        Assert.Equal(Day(3), dto.StartUtc);
    }

    [Fact]
    public async Task Update_StartCanBeCleared()
    {
        var (db, item, userId) = await SetUpAsync();

        var dto = await UpdateAsync(db, item, userId, start: null, deadline: Day(5));

        Assert.Null(dto.StartUtc);
    }

    [Fact]
    public async Task Update_StartAfterTheStoredDeadline_IsPulledBackToTheDeadline()
    {
        var (db, item, userId) = await SetUpAsync();

        // The request sends no deadline, so the stored one stays and the later start is pulled back to it.
        var dto = await UpdateAsync(db, item, userId, start: Day(8), deadline: null);

        Assert.Equal(Day(5), dto.DeadlineUtc);
        Assert.Equal(Day(5), dto.StartUtc);
    }

    [Fact]
    public async Task Update_PlaceholderDeadline_CanBeSetByTheUser_AndTheFlagGoesAway()
    {
        var (db, item, userId) = await SetUpAsync(defaultedDeadline: true);

        var dto = await UpdateAsync(db, item, userId, start: Day(0), deadline: Day(12));

        Assert.Equal(Day(12), dto.DeadlineUtc);
        Assert.False(dto.IsDeadlineDefaulted);
    }

    [Fact]
    public async Task Update_PlaceholderDeadline_SavedUntouched_IsAcceptedAsTheUsersChoice()
    {
        var (db, item, userId) = await SetUpAsync(defaultedDeadline: true);

        var dto = await UpdateAsync(db, item, userId, start: Day(0), deadline: Day(5)); // the form resends what it showed

        Assert.Equal(Day(5), dto.DeadlineUtc);
        Assert.False(dto.IsDeadlineDefaulted);
    }

    [Fact]
    public async Task Update_RealDueDay_CanBeMovedFromTheApp()
    {
        var (db, item, userId) = await SetUpAsync(defaultedDeadline: false);

        var dto = await UpdateAsync(db, item, userId, start: Day(0), deadline: Day(1));

        Assert.Equal(Day(1), dto.DeadlineUtc);
    }

    [Fact]
    public async Task Update_AnImportedCalendarEvent_IsNotTreatedAsAGoogleTask()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        var start = Day(7).AddHours(9);
        var item = new CalendarItem
        {
            TeamId = team.Id, Title = "Meeting", TaskType = TaskType.Event, Status = TaskStatus.Confirmed,
            StartUtc = start, EndUtc = start.AddHours(1), ExternalCalendarEventId = "e1"
        };
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();

        var dto = await new UpdateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new UpdateTaskCommand(item.Id, "x", null, TaskStatus.Confirmed, null, null, 5, null, null, null, owner.Id, TaskType.Event,
                StartUtc: start.AddDays(2), EndUtc: start.AddDays(2).AddHours(1)),
            CancellationToken.None);

        Assert.Equal(start.AddDays(2), dto.StartUtc); // an event edited in the app keeps its own time (the sync brings Google's back only if Google changes it)
        Assert.Equal("x", dto.Title);
        Assert.Null(dto.GoogleTaskListId);
    }

    // ---- what the app reads ----

    [Fact]
    public async Task TeamList_CarriesTheNameOfTheGoogleListEachTaskBelongsTo()
    {
        var (db, item, _) = await SetUpAsync();
        db.CalendarItems.Add(new CalendarItem { TeamId = item.TeamId, Title = "Own task", DeadlineUtc = Day(3) });
        await db.SaveChangesAsync();

        var list = await new GetTasksByTeamQueryHandler(db, Cache).Handle(new GetTasksByTeamQuery(item.TeamId), CancellationToken.None);

        var google = list.Single(t => t.Title == "Google title");
        Assert.Equal("Shopping", google.GoogleTaskListTitle);
        Assert.Equal(item.ExternalTaskListId, google.GoogleTaskListId);
        Assert.True(google.IsExternal);
        var own = list.Single(t => t.Title == "Own task");
        Assert.Null(own.GoogleTaskListId);
        Assert.Null(own.GoogleTaskListTitle);
    }

    [Fact]
    public async Task Detail_CarriesTheListNameAndThePlaceholderFlag()
    {
        var (db, item, _) = await SetUpAsync(defaultedDeadline: true);

        var detail = await new GetTaskDetailQueryHandler(db, Cache).Handle(new GetTaskDetailQuery(item.Id), CancellationToken.None);

        Assert.Equal("Shopping", detail.Task.GoogleTaskListTitle);
        Assert.True(detail.Task.IsDeadlineDefaulted);
    }
}
