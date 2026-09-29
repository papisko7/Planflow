using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Tasks.Queries.GetTaskScoreHistory;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Tests.Application.Tasks.Queries;

public class GetTaskScoreHistoryQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsLogsNewestFirstWithTriggerSource()
    {
        var db = TestDb.Create();
        var team = await TestDb.AddTeamAsync(db);
        var task = new TaskItem { TeamId = team.Id, Title = "T", ImpactScore = 3 };
        db.Tasks.Add(task);
        db.UrgencyScoreLogs.Add(new UrgencyScoreLog { TaskItemId = task.Id, FinalScore = 0.2, TriggerSource = ScoreTriggerSource.ManualCreate, CreatedAtUtc = DateTime.UtcNow.AddHours(-2) });
        db.UrgencyScoreLogs.Add(new UrgencyScoreLog { TaskItemId = task.Id, FinalScore = 0.6, TriggerSource = ScoreTriggerSource.ScheduledRecalculation, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var history = await new GetTaskScoreHistoryQueryHandler(db)
            .Handle(new GetTaskScoreHistoryQuery(task.Id), CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal(0.6, history[0].FinalScore);
        Assert.Equal(ScoreTriggerSource.ScheduledRecalculation, history[0].TriggerSource);
        Assert.Equal(ScoreTriggerSource.ManualCreate, history[1].TriggerSource);
    }

    [Fact]
    public async Task Handle_UnknownTask_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => new GetTaskScoreHistoryQueryHandler(TestDb.Create())
                .Handle(new GetTaskScoreHistoryQuery(Guid.NewGuid()), CancellationToken.None));
    }
}
