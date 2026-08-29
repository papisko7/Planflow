using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PlanFlow.Application.AiPlanner.Common;
using PlanFlow.Application.AiPlanner.Common.Dtos;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.BackgroundJobs;
using PlanFlow.Infrastructure.Persistence;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;
using Xunit;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

public class AnalyzeTasksJobTests
{
    private PlanFlowDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PlanFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PlanFlowDbContext(options);
    }

    [Fact]
    public async Task Execute_WithValidAiResponse_UpdatesTaskAiAssessmentScore()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var taskId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = taskId,
            TeamId = teamId,
            Team = team,
            Title = "Test Task",
            CurrentUrgencyScore = 0.5,
            Status = TaskStatus.Todo,
            AiAssessmentScore = null,
            AiAssessmentAtUtc = null,
            ImpactScore = 5
        };

        context.Add(team);
        context.Add(task);
        await context.SaveChangesAsync();

        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<AnalyzeTasksJob>>();

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
                        SuggestedUrgencyDelta = 0.1,
                        AdjustmentReason = "Test adjustment"
                    }
                },
                SummaryReasoning = "Test summary",
                Confidence = 0.9
            });

        var job = new AnalyzeTasksJob(context, mockAiClient.Object, mockCache.Object, mockLogger.Object);
        var mockJobContext = new Mock<IJobExecutionContext>();
        mockJobContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await job.Execute(mockJobContext.Object);

        // Assert
        var updatedTask = await context.Tasks.FirstAsync(t => t.Id == taskId);
        Assert.NotNull(updatedTask.AiAssessmentScore);
        Assert.Equal(0.6, updatedTask.AiAssessmentScore); // 0.5 + 0.1 delta
        Assert.NotNull(updatedTask.AiAssessmentAtUtc);

        context.Dispose();
    }

    [Fact]
    public async Task Execute_WithAiTimeout_FallsBackToNullAndContinues()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var taskId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = taskId,
            TeamId = teamId,
            Team = team,
            Title = "Test Task",
            CurrentUrgencyScore = 0.5,
            Status = TaskStatus.Todo,
            AiAssessmentScore = null,
            AiAssessmentAtUtc = null,
            ImpactScore = 5
        };

        context.Add(team);
        context.Add(task);
        await context.SaveChangesAsync();

        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<AnalyzeTasksJob>>();

        var mockAiClient = new Mock<IAiPlannerClient>();
        mockAiClient
            .Setup(c => c.SuggestPrioritiesAsync(It.IsAny<SuggestPrioritiesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("Timeout"));

        var job = new AnalyzeTasksJob(context, mockAiClient.Object, mockCache.Object, mockLogger.Object);
        var mockJobContext = new Mock<IJobExecutionContext>();
        mockJobContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await job.Execute(mockJobContext.Object);

        // Assert
        var updatedTask = await context.Tasks.FirstAsync(t => t.Id == taskId);
        Assert.Null(updatedTask.AiAssessmentScore); // Fallback: null (treated as 0 in scoring)
        Assert.NotNull(updatedTask.AiAssessmentAtUtc); // Marked as processed

        context.Dispose();
    }

    [Fact]
    public async Task Execute_WithAiError_FallsBackAndContinues()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var taskId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };
        var task = new TaskItem
        {
            Id = taskId,
            TeamId = teamId,
            Team = team,
            Title = "Test Task",
            CurrentUrgencyScore = 0.5,
            Status = TaskStatus.Todo,
            AiAssessmentScore = null,
            AiAssessmentAtUtc = null,
            ImpactScore = 5
        };

        context.Add(team);
        context.Add(task);
        await context.SaveChangesAsync();

        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<AnalyzeTasksJob>>();

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
        var updatedTask = await context.Tasks.FirstAsync(t => t.Id == taskId);
        Assert.Null(updatedTask.AiAssessmentScore); // Fallback
        Assert.NotNull(updatedTask.AiAssessmentAtUtc); // Marked as processed

        context.Dispose();
    }

    [Fact]
    public async Task Execute_WithNoTasksRequiringAnalysis_ReturnsEarlyWithoutCall()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<AnalyzeTasksJob>>();
        var mockAiClient = new Mock<IAiPlannerClient>();

        var job = new AnalyzeTasksJob(context, mockAiClient.Object, mockCache.Object, mockLogger.Object);
        var mockJobContext = new Mock<IJobExecutionContext>();
        mockJobContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await job.Execute(mockJobContext.Object);

        // Assert
        mockAiClient.Verify(c => c.SuggestPrioritiesAsync(It.IsAny<SuggestPrioritiesRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        context.Dispose();
    }

    [Fact]
    public async Task Execute_WithMultipleBatches_ProcessesAllTasksAndClearsCache()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var nowUtc = DateTime.UtcNow;
        var teamId = Guid.NewGuid();
        var team = new Team { Id = teamId, Name = "Test Team", CreatedAtUtc = nowUtc };

        context.Add(team);

        var tasks = Enumerable.Range(0, 25) // 25 tasks, will be split into batches of 20
            .Select(i => new TaskItem
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                Team = team,
                Title = $"Task {i}",
                CurrentUrgencyScore = 0.5,
                Status = TaskStatus.Todo,
                AiAssessmentScore = null,
                AiAssessmentAtUtc = null,
                ImpactScore = 5
            })
            .ToList();

        context.AddRange(tasks);
        await context.SaveChangesAsync();

        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<AnalyzeTasksJob>>();

        var mockAiClient = new Mock<IAiPlannerClient>();
        mockAiClient
            .Setup(c => c.SuggestPrioritiesAsync(It.IsAny<SuggestPrioritiesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SuggestPrioritiesRequest req, CancellationToken _) => new SuggestPrioritiesResponse
            {
                PriorityAdjustments = req.Tasks.Select(t => new PriorityAdjustment
                {
                    TaskId = t.TaskId,
                    SuggestedUrgencyDelta = 0.05,
                    AdjustmentReason = "Batch analysis"
                }).ToList(),
                SummaryReasoning = "Batch summary",
                Confidence = 0.85
            });

        var job = new AnalyzeTasksJob(context, mockAiClient.Object, mockCache.Object, mockLogger.Object);
        var mockJobContext = new Mock<IJobExecutionContext>();
        mockJobContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await job.Execute(mockJobContext.Object);

        // Assert
        var updatedTasks = await context.Tasks.ToListAsync();
        Assert.All(updatedTasks, task => Assert.NotNull(task.AiAssessmentScore));
        Assert.All(updatedTasks, task => Assert.NotNull(task.AiAssessmentAtUtc));
        mockAiClient.Verify(c => c.SuggestPrioritiesAsync(It.IsAny<SuggestPrioritiesRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2)); // 2 batches

        context.Dispose();
    }

    [Fact]
    public async Task Execute_AnalyzesUnassessedTasks()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var nowUtc = DateTime.UtcNow;
        var team = new Team { Id = Guid.NewGuid(), Name = "Test Team", CreatedAtUtc = nowUtc };

        var unassessedTask1 = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Unassessed 1",
            Status = TaskStatus.Todo,
            CurrentUrgencyScore = 0.5,
            AiAssessmentScore = null, // No assessment
            AiAssessmentAtUtc = null,
            ImpactScore = 5
        };

        var unassessedTask2 = new TaskItem
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Team = team,
            Title = "Unassessed 2",
            Status = TaskStatus.Todo,
            CurrentUrgencyScore = 0.4,
            AiAssessmentScore = null,
            AiAssessmentAtUtc = null,
            ImpactScore = 3
        };

        context.Add(team);
        context.AddRange(unassessedTask1, unassessedTask2);
        await context.SaveChangesAsync();

        var mockCache = new Mock<ICacheService>();
        var mockLogger = new Mock<ILogger<AnalyzeTasksJob>>();

        var mockAiClient = new Mock<IAiPlannerClient>();
        mockAiClient
            .Setup(c => c.SuggestPrioritiesAsync(It.IsAny<SuggestPrioritiesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuggestPrioritiesResponse
            {
                PriorityAdjustments = new List<PriorityAdjustment>
                {
                    new()
                    {
                        TaskId = unassessedTask1.Id,
                        SuggestedUrgencyDelta = 0.1,
                        AdjustmentReason = "Task 1 adjustment"
                    },
                    new()
                    {
                        TaskId = unassessedTask2.Id,
                        SuggestedUrgencyDelta = 0.15,
                        AdjustmentReason = "Task 2 adjustment"
                    }
                },
                SummaryReasoning = "Analyzed batch",
                Confidence = 0.8
            });

        var job = new AnalyzeTasksJob(context, mockAiClient.Object, mockCache.Object, mockLogger.Object);
        var mockJobContext = new Mock<IJobExecutionContext>();
        mockJobContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await job.Execute(mockJobContext.Object);

        // Assert
        var updated1 = await context.Tasks.FirstAsync(t => t.Id == unassessedTask1.Id);
        Assert.Equal(0.6, updated1.AiAssessmentScore); // 0.5 + 0.1 delta
        Assert.NotNull(updated1.AiAssessmentAtUtc);

        var updated2 = await context.Tasks.FirstAsync(t => t.Id == unassessedTask2.Id);
        Assert.Equal(0.55, updated2.AiAssessmentScore); // 0.4 + 0.15 delta
        Assert.NotNull(updated2.AiAssessmentAtUtc);

        mockAiClient.Verify(c => c.SuggestPrioritiesAsync(It.IsAny<SuggestPrioritiesRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        context.Dispose();
    }
}
