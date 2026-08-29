using System.Net;
using PlanFlow.Application.AiPlanner.Common.Dtos;
using PlanFlow.Infrastructure.AiClients;
using Xunit;

namespace PlanFlow.Tests.Infrastructure.AiClients;

public class ClaudeAiPlannerClientTests
{
    [Fact]
    public void Constructor_WithoutApiKey_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ClaudeAiPlannerClient(new HttpClient(), null!));
    }

    [Fact]
    public async Task GeneratePlanAsync_WithInvalidJsonResponse_FallsBackToPlanByUrgency()
    {
        // Arrange
        var mockHttpClient = new MockHttpClientHandler("invalid json response");
        var client = new ClaudeAiPlannerClient(new HttpClient(mockHttpClient), "test-key");

        var taskId = Guid.NewGuid();
        var request = new GeneratePlanRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = taskId,
                    Title = "Test Task",
                    UrgencyScore = 0.8,
                    BlockedTaskCount = 0,
                    ImpactScore = 5
                }
            },
            UserContext = "Any"
        };

        // Act
        var response = await client.GeneratePlanAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.RecommendedTasks);
        Assert.Equal(taskId, response.RecommendedTasks[0].TaskId);
        Assert.Contains("fallback", response.ExecutiveSummary.ToLower());
    }

    [Fact]
    public async Task SuggestPrioritiesAsync_WithValidJsonResponse_ParsesCorrectly()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var validResponse = $$"""
            {
              "priority_adjustments": [
                {
                  "task_id": "{{taskId}}",
                  "suggested_urgency_delta": 0.15,
                  "adjustment_reason": "Test reason"
                }
              ],
              "summary_reasoning": "Test summary",
              "confidence": 0.88
            }
            """;

        var mockHttpClient = new MockHttpClientHandler(validResponse);
        var client = new ClaudeAiPlannerClient(new HttpClient(mockHttpClient), "test-key");

        var request = new SuggestPrioritiesRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = taskId,
                    Title = "Test",
                    UrgencyScore = 0.5,
                    BlockedTaskCount = 0,
                    ImpactScore = 5
                }
            },
            UserContext = "Any"
        };

        // Act
        var response = await client.SuggestPrioritiesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(0.88, response.Confidence);
        Assert.Equal("Test summary", response.SummaryReasoning);
        Assert.Single(response.PriorityAdjustments);
        Assert.Equal(0.15, response.PriorityAdjustments[0].SuggestedUrgencyDelta);
    }

    [Fact]
    public async Task GeneratePlanAsync_WithMissingFields_FallsBackGracefully()
    {
        // Arrange (response missing "confidence" field)
        var taskId = Guid.NewGuid();
        var incompleteResponse = $$"""
            {
              "recommended_tasks": [
                {
                  "task_id": "{{taskId}}",
                  "priority_rank": 1,
                  "suggested_urgency_score": 0.9,
                  "reasoning": "Test"
                }
              ],
              "executive_summary": "Plan"
            }
            """;

        var mockHttpClient = new MockHttpClientHandler(incompleteResponse);
        var client = new ClaudeAiPlannerClient(new HttpClient(mockHttpClient), "test-key");

        var request = new GeneratePlanRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = taskId,
                    Title = "Test",
                    UrgencyScore = 0.8,
                    BlockedTaskCount = 0,
                    ImpactScore = 5
                }
            },
            UserContext = "Any"
        };

        // Act
        var response = await client.GeneratePlanAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(0.75, response.Confidence); // Default fallback confidence
    }

    [Fact]
    public async Task GeneratePlanAsync_ClampsSuggestedScoreTo01Range()
    {
        // Arrange (invalid score > 1.0)
        var taskId = Guid.NewGuid();
        var responseWithInvalidScore = $$"""
            {
              "recommended_tasks": [
                {
                  "task_id": "{{taskId}}",
                  "priority_rank": 1,
                  "suggested_urgency_score": 1.5,
                  "reasoning": "Test"
                }
              ],
              "executive_summary": "Plan",
              "confidence": 0.8
            }
            """;

        var mockHttpClient = new MockHttpClientHandler(responseWithInvalidScore);
        var client = new ClaudeAiPlannerClient(new HttpClient(mockHttpClient), "test-key");

        var request = new GeneratePlanRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = taskId,
                    Title = "Test",
                    UrgencyScore = 0.8,
                    BlockedTaskCount = 0,
                    ImpactScore = 5
                }
            },
            UserContext = "Any"
        };

        // Act
        var response = await client.GeneratePlanAsync(request, CancellationToken.None);

        // Assert
        Assert.True(response.RecommendedTasks[0].SuggestedUrgencyScore <= 1.0);
    }

    [Fact]
    public async Task SuggestPrioritiesAsync_ClampsDeltaToValidRange()
    {
        // Arrange (delta > 1.0 should clamp)
        var taskId = Guid.NewGuid();
        var responseWithInvalidDelta = $$"""
            {
              "priority_adjustments": [
                {
                  "task_id": "{{taskId}}",
                  "suggested_urgency_delta": 2.5,
                  "adjustment_reason": "Test"
                }
              ],
              "summary_reasoning": "Summary",
              "confidence": 0.8
            }
            """;

        var mockHttpClient = new MockHttpClientHandler(responseWithInvalidDelta);
        var client = new ClaudeAiPlannerClient(new HttpClient(mockHttpClient), "test-key");

        var request = new SuggestPrioritiesRequest
        {
            Tasks = new List<PlanTaskContext>
            {
                new()
                {
                    TaskId = taskId,
                    Title = "Test",
                    UrgencyScore = 0.5,
                    BlockedTaskCount = 0,
                    ImpactScore = 5
                }
            },
            UserContext = "Any"
        };

        // Act
        var response = await client.SuggestPrioritiesAsync(request, CancellationToken.None);

        // Assert
        Assert.True(response.PriorityAdjustments[0].SuggestedUrgencyDelta <= 1.0);
    }
}

/// <summary>
/// Mock HttpMessageHandler for testing Claude client with controlled responses.
/// Wraps the given JSON payload in Anthropic API response structure.
/// </summary>
public class MockHttpClientHandler : HttpMessageHandler
{
    private readonly string _responseContent;

    public MockHttpClientHandler(string responseContent)
    {
        _responseContent = responseContent;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Wrap the response in Anthropic API structure: { "content": [{ "text": "<response>" }] }
        var wrappedResponse = $$"""
            {
              "content": [
                {
                  "text": {{System.Text.Json.JsonSerializer.Serialize(_responseContent)}}
                }
              ]
            }
            """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(wrappedResponse)
        };

        return Task.FromResult(response);
    }
}
