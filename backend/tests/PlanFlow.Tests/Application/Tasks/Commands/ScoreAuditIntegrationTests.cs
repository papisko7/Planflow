using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PlanFlow.Application.AiPlanner.Common;
using PlanFlow.Application.AiPlanner.Common.Dtos;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Commands.CreateTask;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using Quartz;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Tasks.Commands;

public class ScoreAuditIntegrationTests
{
    private PlanFlowDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PlanFlowDbContext(options);
    }

    [Fact]
    public async Task CreateTaskCommand_CreatesUrgencyScoreLogWithManualCreateTrigger()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var teamId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        context.Add(team);
        context.SaveChanges();

        var handler = new CreateTaskCommandHandler(context, mockCache.Object);
        var command = new CreateTaskCommand(
            teamId,
            "New Task",
            "Test description",
            null,
            nowUtc.AddDays(7),
            5,
            null,
            createdByUserId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var scoreLogs = context.UrgencyScoreLogs.Where(l => l.TaskItemId == result.Id).ToList();
        Assert.Single(scoreLogs);
        Assert.Equal(ScoreTriggerSource.ManualCreate, scoreLogs[0].TriggerSource);
        Assert.True(scoreLogs[0].FinalScore >= 0.0 && scoreLogs[0].FinalScore <= 1.0);

        context.Dispose();
    }

    [Fact]
    public async Task UpdateTaskCommand_CreatesUrgencyScoreLogWithManualUpdateTrigger()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var nowUtc = DateTime.UtcNow;
        var teamId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var updatedByUserId = Guid.NewGuid();

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = taskId,
            TeamId = teamId,
            Team = team,
            Title = "Original Title",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            CurrentUrgencyScore = 0.5,
            AiAssessmentScore = null
        };

        context.Add(team);
        context.Add(task);
        context.SaveChanges();

        var handler = new UpdateTaskCommandHandler(context, mockCache.Object);
        var command = new UpdateTaskCommand(
            taskId,
            "Updated Title",
            null,
            TaskStatus.InProgress,
            null,
            null,
            8,
            null,
            null,
            null,
            updatedByUserId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var scoreLogs = context.UrgencyScoreLogs
            .Where(l => l.TaskItemId == taskId)
            .OrderBy(l => l.CreatedAtUtc)
            .ToList();

        Assert.NotEmpty(scoreLogs);
        // Last log should be from the update
        Assert.Equal(ScoreTriggerSource.ManualUpdate, scoreLogs[scoreLogs.Count - 1].TriggerSource);

        context.Dispose();
    }

    [Fact]
    public async Task PrioritizationJob_CreatesUrgencyScoreLogWithScheduledRecalculationTrigger()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<PrioritizationJob>>();
        var nowUtc = DateTime.UtcNow;
        var teamId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = taskId,
            TeamId = teamId,
            Team = team,
            Title = "Task for Prioritization",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            CurrentUrgencyScore = 0.3,
            AiAssessmentScore = null
        };

        context.Add(team);
        context.Add(task);
        context.SaveChanges();

        var job = new PrioritizationJob(context, mockCache.Object, mockLogger.Object);
        var mockJobContext = new Mock<IJobExecutionContext>();
        mockJobContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await job.Execute(mockJobContext.Object);

        // Assert
        var scoreLogs = context.UrgencyScoreLogs
            .Where(l => l.TaskItemId == taskId)
            .OrderBy(l => l.CreatedAtUtc)
            .ToList();

        Assert.NotEmpty(scoreLogs);
        Assert.Contains(scoreLogs, l => l.TriggerSource == ScoreTriggerSource.ScheduledRecalculation);

        context.Dispose();
    }

    [Fact]
    public async Task AnalyzeTasksJob_WithSuccessfulAiResponse_CreatesAuditLogWithAiAssessmentTrigger()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<AnalyzeTasksJob>>();
        var nowUtc = DateTime.UtcNow;
        var teamId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = taskId,
            TeamId = teamId,
            Team = team,
            Title = "Task for AI Analysis",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            CurrentUrgencyScore = 0.5,
            AiAssessmentScore = null,
            AiAssessmentAtUtc = null
        };

        context.Add(team);
        context.Add(task);
        context.SaveChanges();

        var mockAiClient = new Mock<IAiPlannerClient>();
        mockAiClient
            .Setup(c => c.SuggestPrioritiesAsync(It.IsAny<SuggestPrioritiesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuggestPrioritiesResponse
            {
                PriorityAdjustments = new List<PriorityAdjustment>
                {
                    new()
                    {
                        TaskId = taskId,
                        SuggestedUrgencyDelta = 0.2,
                        AdjustmentReason = "High priority based on context"
                    }
                },
                SummaryReasoning = "Analysis summary",
                Confidence = 0.85
            });

        var job = new AnalyzeTasksJob(context, mockAiClient.Object, mockCache.Object, mockLogger.Object);
        var mockJobContext = new Mock<IJobExecutionContext>();
        mockJobContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await job.Execute(mockJobContext.Object);

        // Assert
        var scoreLogs = context.UrgencyScoreLogs
            .Where(l => l.TaskItemId == taskId)
            .OrderBy(l => l.CreatedAtUtc)
            .ToList();

        Assert.NotEmpty(scoreLogs);
        var aiLog = scoreLogs.FirstOrDefault(l => l.TriggerSource == ScoreTriggerSource.AiAssessment);
        Assert.NotNull(aiLog);
        Assert.True(aiLog.AiComponent > 0.0, "AI component should be positive after assessment");
        Assert.False(aiLog.AiFallbackUsed);

        context.Dispose();
    }

    [Fact]
    public async Task AnalyzeTasksJob_WithAiTimeout_CreatesAuditLogWithAiFallbackTrigger()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<AnalyzeTasksJob>>();
        var nowUtc = DateTime.UtcNow;
        var teamId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = taskId,
            TeamId = teamId,
            Team = team,
            Title = "Task for AI Timeout",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            CurrentUrgencyScore = 0.5,
            AiAssessmentScore = null,
            AiAssessmentAtUtc = null
        };

        context.Add(team);
        context.Add(task);
        context.SaveChanges();

        var mockAiClient = new Mock<IAiPlannerClient>();
        mockAiClient
            .Setup(c => c.SuggestPrioritiesAsync(It.IsAny<SuggestPrioritiesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("AI request timed out"));

        var job = new AnalyzeTasksJob(context, mockAiClient.Object, mockCache.Object, mockLogger.Object);
        var mockJobContext = new Mock<IJobExecutionContext>();
        mockJobContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await job.Execute(mockJobContext.Object);

        // Assert
        var scoreLogs = context.UrgencyScoreLogs
            .Where(l => l.TaskItemId == taskId)
            .OrderBy(l => l.CreatedAtUtc)
            .ToList();

        Assert.NotEmpty(scoreLogs);
        var fallbackLog = scoreLogs.FirstOrDefault(l => l.TriggerSource == ScoreTriggerSource.AiFallback);
        Assert.NotNull(fallbackLog);
        Assert.Equal(0.0, fallbackLog.AiComponent);
        Assert.True(fallbackLog.AiFallbackUsed);

        context.Dispose();
    }

    [Fact]
    public async Task AnalyzeTasksJob_WithAiError_CreatesAuditLogWithAiFallbackTrigger()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<AnalyzeTasksJob>>();
        var nowUtc = DateTime.UtcNow;
        var teamId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = taskId,
            TeamId = teamId,
            Team = team,
            Title = "Task for AI Error",
            Status = TaskStatus.Todo,
            ImpactScore = 5,
            CurrentUrgencyScore = 0.5,
            AiAssessmentScore = null,
            AiAssessmentAtUtc = null
        };

        context.Add(team);
        context.Add(task);
        context.SaveChanges();

        var mockAiClient = new Mock<IAiPlannerClient>();
        mockAiClient
            .Setup(c => c.SuggestPrioritiesAsync(It.IsAny<SuggestPrioritiesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("500 Internal Server Error"));

        var job = new AnalyzeTasksJob(context, mockAiClient.Object, mockCache.Object, mockLogger.Object);
        var mockJobContext = new Mock<IJobExecutionContext>();
        mockJobContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await job.Execute(mockJobContext.Object);

        // Assert
        var scoreLogs = context.UrgencyScoreLogs
            .Where(l => l.TaskItemId == taskId)
            .OrderBy(l => l.CreatedAtUtc)
            .ToList();

        Assert.NotEmpty(scoreLogs);
        var fallbackLog = scoreLogs.FirstOrDefault(l => l.TriggerSource == ScoreTriggerSource.AiFallback);
        Assert.NotNull(fallbackLog);
        Assert.True(fallbackLog.AiFallbackUsed);

        context.Dispose();
    }

    [Fact]
    public async Task ScoreHistoryForTask_TracksAllCalculations_InChronologicalOrder()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var nowUtc = new DateTime(2025, 08, 30, 12, 0, 0, DateTimeKind.Utc);
        var teamId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        context.Add(team);
        context.SaveChanges();

        // Create task via handler (generates ManualCreate log)
        var createHandler = new CreateTaskCommandHandler(context, mockCache.Object);
        var createdByUserId = Guid.NewGuid();
        var createCmd = new CreateTaskCommand(
            teamId,
            "Track Me",
            null,
            null,
            null,
            5,
            null,
            createdByUserId);

        var taskResult = await createHandler.Handle(createCmd, CancellationToken.None);

        // Update task (generates ManualUpdate log)
        var updateHandler = new UpdateTaskCommandHandler(context, mockCache.Object);
        var updatedByUserId = Guid.NewGuid();
        var updateCmd = new UpdateTaskCommand(
            taskResult.Id,
            "Updated Title",
            null,
            TaskStatus.Todo,
            null,
            null,
            8,
            null,
            null,
            null,
            updatedByUserId);

        await updateHandler.Handle(updateCmd, CancellationToken.None);

        // Act: Retrieve full score history
        var history = context.UrgencyScoreLogs
            .Where(l => l.TaskItemId == taskResult.Id)
            .OrderBy(l => l.CreatedAtUtc)
            .ToList();

        // Assert
        Assert.Equal(2, history.Count);
        Assert.Equal(ScoreTriggerSource.ManualCreate, history[0].TriggerSource);
        Assert.Equal(ScoreTriggerSource.ManualUpdate, history[1].TriggerSource);
        Assert.True(history[0].CreatedAtUtc <= history[1].CreatedAtUtc);

        context.Dispose();
    }

    [Fact]
    public async Task MultipleTasksAuditedIndependently()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var nowUtc = DateTime.UtcNow;
        var teamId = Guid.NewGuid();

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        context.Add(team);
        context.SaveChanges();

        var handler = new CreateTaskCommandHandler(context, mockCache.Object);

        // Act: Create multiple tasks
        var userId1 = Guid.NewGuid();
        var task1 = await handler.Handle(new CreateTaskCommand(
            teamId,
            "Task 1",
            null,
            null,
            null,
            5,
            null,
            userId1), CancellationToken.None);

        var userId2 = Guid.NewGuid();
        var task2 = await handler.Handle(new CreateTaskCommand(
            teamId,
            "Task 2",
            null,
            null,
            null,
            7,
            null,
            userId2), CancellationToken.None);

        // Assert
        var logs1 = context.UrgencyScoreLogs.Where(l => l.TaskItemId == task1.Id).ToList();
        var logs2 = context.UrgencyScoreLogs.Where(l => l.TaskItemId == task2.Id).ToList();

        Assert.Single(logs1);
        Assert.Single(logs2);
        Assert.Equal(ScoreTriggerSource.ManualCreate, logs1[0].TriggerSource);
        Assert.Equal(ScoreTriggerSource.ManualCreate, logs2[0].TriggerSource);

        context.Dispose();
    }
}
