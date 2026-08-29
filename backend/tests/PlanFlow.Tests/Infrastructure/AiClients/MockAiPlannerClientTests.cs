using PlanFlow.Application.AiPlanner.Common;
using PlanFlow.Application.AiPlanner.Common.Dtos;
using PlanFlow.Infrastructure.AiClients;
using Xunit;

namespace PlanFlow.Tests.Infrastructure.AiClients;

public class MockAiPlannerClientTests
{
    private readonly MockAiPlannerClient _client = new();

    [Fact]
    public async Task GeneratePlanAsync_WithValidRequest_ReturnsRecommendedTasks()
    {
        // Arrange
        var request = new GeneratePlanRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = Guid.NewGuid(),
                    Title = "High Priority Task",
                    UrgencyScore = 0.9,
                    BlockedTaskCount = 2,
                    ImpactScore = 9,
                    DeadlineUtc = DateTime.UtcNow.AddDays(1)
                },
                new()
                {
                    TaskId = Guid.NewGuid(),
                    Title = "Low Priority Task",
                    UrgencyScore = 0.2,
                    BlockedTaskCount = 0,
                    ImpactScore = 2,
                    DeadlineUtc = DateTime.UtcNow.AddDays(30)
                }
            },
            UserContext = "Focus on high-impact tasks"
        };

        // Act
        var response = await _client.GeneratePlanAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.RecommendedTasks);
        Assert.Equal(2, response.RecommendedTasks.Count);
        // Tasks ranked by urgency: high priority first
        Assert.Equal(1, response.RecommendedTasks[0].PriorityRank);
        Assert.True(response.RecommendedTasks[0].SuggestedUrgencyScore >= 0.9);
    }

    [Fact]
    public async Task GeneratePlanAsync_TasksOrderedByUrgency_HighestFirst()
    {
        // Arrange
        var taskHigh = new PlanTaskContext
        {
            TaskId = Guid.NewGuid(),
            Title = "High Urgency",
            UrgencyScore = 0.95,
            BlockedTaskCount = 5,
            ImpactScore = 10
        };
        var taskLow = new PlanTaskContext
        {
            TaskId = Guid.NewGuid(),
            Title = "Low Urgency",
            UrgencyScore = 0.1,
            BlockedTaskCount = 0,
            ImpactScore = 1
        };

        var request = new GeneratePlanRequest
        {
            Tasks = new List<PlanTaskContext> { taskLow, taskHigh },
            UserContext = "Any"
        };

        // Act
        var response = await _client.GeneratePlanAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(taskHigh.TaskId, response.RecommendedTasks[0].TaskId);
        Assert.Equal(taskLow.TaskId, response.RecommendedTasks[1].TaskId);
    }

    [Fact]
    public async Task GeneratePlanAsync_WithCustomResponse_ReturnsCustomResponse()
    {
        // Arrange
        var customResponse = new GeneratePlanResponse
        {
            RecommendedTasks = new List<RecommendedTask>
            {
                new()
                {
                    TaskId = Guid.NewGuid(),
                    PriorityRank = 1,
                    SuggestedUrgencyScore = 0.88,
                    Reasoning = "Custom reasoning from test"
                }
            },
            ExecutiveSummary = "Custom plan",
            Confidence = 0.95
        };
        _client.SetNextGeneratePlanResponse(customResponse);

        var request = new GeneratePlanRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = Guid.NewGuid(),
                    Title = "Any",
                    UrgencyScore = 0.5,
                    BlockedTaskCount = 0,
                    ImpactScore = 5
                }
            },
            UserContext = "Any"
        };

        // Act
        var response = await _client.GeneratePlanAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(customResponse.ExecutiveSummary, response.ExecutiveSummary);
        Assert.Equal(customResponse.Confidence, response.Confidence);
    }

    [Fact]
    public async Task GeneratePlanAsync_WhenExceptionForced_Throws()
    {
        // Arrange
        var testException = new InvalidOperationException("Test forced error");
        _client.SetNextException(testException);

        var request = new GeneratePlanRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = Guid.NewGuid(),
                    Title = "Any",
                    UrgencyScore = 0.5,
                    BlockedTaskCount = 0,
                    ImpactScore = 5
                }
            },
            UserContext = "Any"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _client.GeneratePlanAsync(request, CancellationToken.None));
        Assert.Equal("Test forced error", ex.Message);
    }

    [Fact]
    public async Task SuggestPrioritiesAsync_WithValidRequest_ReturnsPriorityAdjustments()
    {
        // Arrange
        var taskId1 = Guid.NewGuid();
        var taskId2 = Guid.NewGuid();

        var request = new SuggestPrioritiesRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = taskId1,
                    Title = "Blocking Task",
                    UrgencyScore = 0.5,
                    BlockedTaskCount = 3,
                    ImpactScore = 8
                },
                new()
                {
                    TaskId = taskId2,
                    Title = "Non-blocking Task",
                    UrgencyScore = 0.5,
                    BlockedTaskCount = 0,
                    ImpactScore = 3
                }
            },
            UserContext = "Focus on unblocking others"
        };

        // Act
        var response = await _client.SuggestPrioritiesAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(2, response.PriorityAdjustments.Count);
        // Blocking task should get higher delta
        var blockingAdjustment = response.PriorityAdjustments.First(a => a.TaskId == taskId1);
        var nonBlockingAdjustment = response.PriorityAdjustments.First(a => a.TaskId == taskId2);
        Assert.True(blockingAdjustment.SuggestedUrgencyDelta > nonBlockingAdjustment.SuggestedUrgencyDelta);
    }

    [Fact]
    public async Task SuggestPrioritiesAsync_WithCustomResponse_ReturnsCustomResponse()
    {
        // Arrange
        var customResponse = new SuggestPrioritiesResponse
        {
            PriorityAdjustments = new List<PriorityAdjustment>
            {
                new()
                {
                    TaskId = Guid.NewGuid(),
                    SuggestedUrgencyDelta = 0.15,
                    AdjustmentReason = "Custom reason"
                }
            },
            SummaryReasoning = "Custom summary",
            Confidence = 0.92
        };
        _client.SetNextSuggestPrioritiesResponse(customResponse);

        var request = new SuggestPrioritiesRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = Guid.NewGuid(),
                    Title = "Any",
                    UrgencyScore = 0.5,
                    BlockedTaskCount = 0,
                    ImpactScore = 5
                }
            },
            UserContext = "Any"
        };

        // Act
        var response = await _client.SuggestPrioritiesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(customResponse.SummaryReasoning, response.SummaryReasoning);
        Assert.Equal(customResponse.Confidence, response.Confidence);
    }

    [Fact]
    public async Task SuggestPrioritiesAsync_WhenExceptionForced_Throws()
    {
        // Arrange
        var testException = new TimeoutException("Connection timeout");
        _client.SetNextException(testException);

        var request = new SuggestPrioritiesRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = Guid.NewGuid(),
                    Title = "Any",
                    UrgencyScore = 0.5,
                    BlockedTaskCount = 0,
                    ImpactScore = 5
                }
            },
            UserContext = "Any"
        };

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _client.SuggestPrioritiesAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GeneratePlanAsync_EmptyTasks_ReturnsEmptyPlan()
    {
        // Arrange
        var request = new GeneratePlanRequest
        {
            Tasks = new List<PlanTaskContext>(),
            UserContext = "Any"
        };

        // Act
        var response = await _client.GeneratePlanAsync(request, CancellationToken.None);

        // Assert
        Assert.Empty(response.RecommendedTasks);
    }

    [Fact]
    public async Task GeneratePlanAsync_MultipleCallsWithoutCustomResponse_ReturnsDeterministicResults()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var request = new GeneratePlanRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = taskId,
                    Title = "Test Task",
                    UrgencyScore = 0.7,
                    BlockedTaskCount = 2,
                    ImpactScore = 6
                }
            },
            UserContext = "Any"
        };

        // Act
        var response1 = await _client.GeneratePlanAsync(request, CancellationToken.None);
        var response2 = await _client.GeneratePlanAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(response1.RecommendedTasks[0].TaskId, response2.RecommendedTasks[0].TaskId);
        Assert.Equal(response1.RecommendedTasks[0].PriorityRank, response2.RecommendedTasks[0].PriorityRank);
    }
}
