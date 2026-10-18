using Moq;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks;

/// <summary>Step 7.4: an imported item flagged "incomplete" stops being flagged as soon as the user edits and saves it.</summary>
public class IncompleteDataFlagTests
{
    [Fact]
    public async Task Update_ClearsTheIncompleteFlag()
    {
        var db = TestDb.Create();
        var owner = await TestDb.AddUserAsync(db);
        var team = await TestDb.AddTeamAsync(db, (owner, TeamRole.Owner));
        var start = DateTime.UtcNow.AddDays(1);
        var item = new CalendarItem
        {
            TeamId = team.Id, AssignedUserId = owner.Id, Title = "Imported", TaskType = TaskType.Event,
            StartUtc = start, EndUtc = start.AddHours(1), IsDataIncomplete = true
        };
        db.CalendarItems.Add(item);
        await db.SaveChangesAsync();

        var dto = await new UpdateTaskCommandHandler(db, new Mock<ICacheService>().Object, new NotificationService(db)).Handle(
            new UpdateTaskCommand(item.Id, "Imported", null, TaskStatus.Backlog, owner.Id, null, 7, null, null, null, owner.Id,
                TaskType.Event, StartUtc: start, EndUtc: start.AddHours(1)),
            CancellationToken.None);

        Assert.False(dto.IsDataIncomplete);
        Assert.False(db.CalendarItems.Single().IsDataIncomplete);
        Assert.Equal(7, db.CalendarItems.Single().ImpactScore);
    }
}
