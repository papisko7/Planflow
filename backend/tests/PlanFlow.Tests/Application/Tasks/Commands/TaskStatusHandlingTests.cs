using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

/// <summary>Status per item type: a Task starts in Backlog, an Event is Confirmed, and a status of the wrong type is replaced.</summary>
public class TaskStatusHandlingTests
{
    private static ICacheService Cache => new Mock<ICacheService>().Object;

    private static async Task<(PlanFlow.Infrastructure.Persistence.PlanFlowDbContext Db, Guid TeamId, Guid UserId)> SetUpAsync()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        return (db, team.Id, owner.Id);
    }

    [Fact]
    public async Task CreateTask_StartsInBacklog()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await new CreateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new CreateTaskCommand(teamId, "Report", null, null, DateTime.UtcNow.AddDays(2), 5, null, userId), CancellationToken.None);

        Assert.Equal(TaskStatus.Backlog, dto.Status);
    }

    [Fact]
    public async Task CreateEvent_StartsConfirmed()
    {
        var (db, teamId, userId) = await SetUpAsync();

        var dto = await new CreateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new CreateTaskCommand(teamId, "Standup", null, null, null, 5, null, userId,
                TaskType.Event, StartUtc: DateTime.UtcNow.AddHours(2), EndUtc: DateTime.UtcNow.AddHours(3)),
            CancellationToken.None);

        Assert.Equal(TaskStatus.Confirmed, dto.Status);
    }

    private static async Task<TaskStatus> UpdateStatusAsync(
        PlanFlow.Infrastructure.Persistence.PlanFlowDbContext db, CalendarItem item, Guid userId, TaskType type, TaskStatus status)
    {
        var due = DateTime.UtcNow.AddDays(2);
        var dto = await new UpdateTaskCommandHandler(db, Cache, new NotificationService(db)).Handle(
            new UpdateTaskCommand(item.Id, item.Title, null, status, null, type == TaskType.Task ? due : null, 5, null, null, null, userId,
                type, StartUtc: type == TaskType.Event ? due : null, EndUtc: type == TaskType.Event ? due.AddHours(1) : null),
            CancellationToken.None);
        return dto.Status;
    }

    private static async Task<CalendarItem> AddItemAsync(
        PlanFlow.Infrastructure.Persistence.PlanFlowDbContext db, Guid teamId, TaskType type, TaskStatus status)
    {
        var item = new CalendarItem
        {
            TeamId = teamId, Title = "Item", TaskType = type, Status = status,
            DeadlineUtc = type == TaskType.Task ? DateTime.UtcNow.AddDays(2) : null,
            StartUtc = type == TaskType.Event ? DateTime.UtcNow.AddDays(2) : null
        };
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task UpdateTask_LegalStatus_IsKept()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var item = await AddItemAsync(db, teamId, TaskType.Task, TaskStatus.Backlog);

        Assert.Equal(TaskStatus.Completed, await UpdateStatusAsync(db, item, userId, TaskType.Task, TaskStatus.Completed));
    }

    [Fact]
    public async Task UpdateEvent_WithATaskStatus_FallsBackToConfirmed()
    {
        // An older app build (or a stale form) sends "Backlog" for an event: it must not fail, the event just stays Confirmed.
        var (db, teamId, userId) = await SetUpAsync();
        var item = await AddItemAsync(db, teamId, TaskType.Event, TaskStatus.Tentative);

        Assert.Equal(TaskStatus.Confirmed, await UpdateStatusAsync(db, item, userId, TaskType.Event, TaskStatus.Backlog));
    }

    [Fact]
    public async Task UpdateEvent_TentativeAndCancelled_AreKept()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var item = await AddItemAsync(db, teamId, TaskType.Event, TaskStatus.Confirmed);

        Assert.Equal(TaskStatus.Tentative, await UpdateStatusAsync(db, item, userId, TaskType.Event, TaskStatus.Tentative));
        Assert.Equal(TaskStatus.Cancelled, await UpdateStatusAsync(db, item, userId, TaskType.Event, TaskStatus.Cancelled));
    }

    [Fact]
    public async Task ConvertingATaskToAnEvent_ResetsTheStatusToConfirmed()
    {
        var (db, teamId, userId) = await SetUpAsync();
        var item = await AddItemAsync(db, teamId, TaskType.Task, TaskStatus.InProgress);

        Assert.Equal(TaskStatus.Confirmed, await UpdateStatusAsync(db, item, userId, TaskType.Event, TaskStatus.InProgress));
    }

    [Fact]
    public void Validator_RejectsTheRetiredBlockedStatus()
    {
        var command = new UpdateTaskCommand(Guid.NewGuid(), "T", null, (TaskStatus)2, null, null, 1, null, null, null, null);

        var result = new UpdateTaskCommandValidator().Validate(command);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateTaskCommand.Status));
    }
}
