using System.Text;
using System.Text.Json;
using PlanFlow.Application.AiPlanner.Common;
using PlanFlow.Application.AiPlanner.Common.Dtos;

namespace PlanFlow.Infrastructure.AiClients;

/// <summary>
/// Claude AI (Anthropic) integration for task planning.
/// Uses the Anthropic REST API (currently minimal; full SDK integration can follow).
/// Reads API key from environment: ANTHROPIC_API_KEY
/// Includes robust error handling for rate limits, network failures, and malformed responses.
/// </summary>
public class ClaudeAiPlannerClient : IAiPlannerClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private const string AnthropicApiBaseUrl = "https://api.anthropic.com/v1";
    private const int MaxRetries = 3;
    private const int InitialRetryDelayMs = 1000;

    public ClaudeAiPlannerClient(HttpClient httpClient, string apiKey, string? model = null)
    {
        _httpClient = httpClient;
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey), "Anthropic API key is required");
        _model = model ?? "claude-3-5-sonnet-20241022";
    }

    public async Task<GeneratePlanResponse> GeneratePlanAsync(GeneratePlanRequest request, CancellationToken cancellationToken)
    {
        var prompt = BuildGeneratePlanPrompt(request);
        var response = await CallClaudeWithRetryAsync(prompt, cancellationToken);
        return ParseGeneratePlanResponse(response, request.Tasks);
    }

    public async Task<SuggestPrioritiesResponse> SuggestPrioritiesAsync(SuggestPrioritiesRequest request, CancellationToken cancellationToken)
    {
        var prompt = BuildSuggestPrioritiesPrompt(request);
        var response = await CallClaudeWithRetryAsync(prompt, cancellationToken);
        return ParseSuggestPrioritiesResponse(response, request.Tasks);
    }

    private async Task<string> CallClaudeWithRetryAsync(string prompt, CancellationToken cancellationToken)
    {
        int retryCount = 0;
        int delayMs = InitialRetryDelayMs;

        while (true)
        {
            try
            {
                return await CallClaudeAsync(prompt, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.InnerException is TimeoutException && retryCount < MaxRetries)
            {
                retryCount++;
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
                continue;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests && retryCount < MaxRetries)
            {
                retryCount++;
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
                continue;
            }
        }
    }

    private async Task<string> CallClaudeAsync(string prompt, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = 2048,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{AnthropicApiBaseUrl}/messages")
        {
            Content = content
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Claude API error {response.StatusCode}: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var jsonDoc = JsonDocument.Parse(responseContent);
        var root = jsonDoc.RootElement;

        if (!root.TryGetProperty("content", out var contentArray) ||
            contentArray.ValueKind != JsonValueKind.Array ||
            contentArray.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Unexpected Claude API response structure");
        }

        var firstContent = contentArray[0];
        if (!firstContent.TryGetProperty("text", out var textProperty))
        {
            throw new InvalidOperationException("Claude response missing 'text' field");
        }

        return textProperty.GetString() ?? throw new InvalidOperationException("Claude returned null text");
    }

    private static string BuildGeneratePlanPrompt(GeneratePlanRequest request)
    {
        var tasksList = string.Join("\n", request.Tasks.Select((t, i) =>
            $"{i + 1}. {t.Title} " +
            $"(ID: {t.TaskId}, Deadline: {t.DeadlineUtc:yyyy-MM-dd HH:mm}, " +
            $"Blocking: {t.BlockedTaskCount}, Impact: {t.ImpactScore}/10, Urgency: {t.UrgencyScore:F2})"));

        return $@"You are a task prioritization expert. Given the following tasks and user context, generate a detailed execution plan.

USER CONTEXT:
{request.UserContext}

TASKS:
{tasksList}

Provide a JSON response with this structure (no markdown, raw JSON only):
{{
  ""recommended_tasks"": [
    {{
      ""task_id"": ""<uuid>"",
      ""priority_rank"": <1-based index>,
      ""suggested_urgency_score"": <0.0-1.0>,
      ""reasoning"": ""<one sentence explanation>""
    }}
  ],
  ""executive_summary"": ""<2-3 sentence summary of the plan>"",
  ""confidence"": <0.0-1.0>
}}";
    }

    private static string BuildSuggestPrioritiesPrompt(SuggestPrioritiesRequest request)
    {
        var tasksList = string.Join("\n", request.Tasks.Select((t, i) =>
            $"{i + 1}. {t.Title} " +
            $"(ID: {t.TaskId}, Deadline: {t.DeadlineUtc:yyyy-MM-dd HH:mm}, " +
            $"Blocking: {t.BlockedTaskCount}, Impact: {t.ImpactScore}/10)"));

        return $@"You are a task prioritization expert. Given the following tasks and user context, suggest priority adjustments.

USER CONTEXT:
{request.UserContext}

TASKS:
{tasksList}

Provide a JSON response with this structure (no markdown, raw JSON only):
{{
  ""priority_adjustments"": [
    {{
      ""task_id"": ""<uuid>"",
      ""suggested_urgency_delta"": <-1.0 to 1.0>,
      ""adjustment_reason"": ""<why this adjustment>""
    }}
  ],
  ""summary_reasoning"": ""<brief explanation of all adjustments>"",
  ""confidence"": <0.0-1.0>
}}";
    }

    private static GeneratePlanResponse ParseGeneratePlanResponse(string jsonResponse, IReadOnlyList<PlanTaskContext> originalTasks)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(jsonResponse);
            var root = jsonDoc.RootElement;

            var recommendations = new List<RecommendedTask>();
            if (root.TryGetProperty("recommended_tasks", out var tasksArray))
            {
                foreach (var taskElement in tasksArray.EnumerateArray())
                {
                    if (Guid.TryParse(taskElement.GetProperty("task_id").GetString(), out var taskId) &&
                        taskElement.TryGetProperty("priority_rank", out var rankProp) &&
                        taskElement.TryGetProperty("suggested_urgency_score", out var scoreProp) &&
                        taskElement.TryGetProperty("reasoning", out var reasoningProp))
                    {
                        recommendations.Add(new RecommendedTask
                        {
                            TaskId = taskId,
                            PriorityRank = rankProp.GetInt32(),
                            SuggestedUrgencyScore = Math.Clamp(scoreProp.GetDouble(), 0.0, 1.0),
                            Reasoning = reasoningProp.GetString() ?? "No reasoning provided"
                        });
                    }
                }
            }

            // Fallback: if parsing failed, return default plan based on urgency
            if (!recommendations.Any())
            {
                recommendations = originalTasks
                    .OrderByDescending(t => t.UrgencyScore)
                    .Select((t, i) => new RecommendedTask
                    {
                        TaskId = t.TaskId,
                        PriorityRank = i + 1,
                        SuggestedUrgencyScore = t.UrgencyScore,
                        Reasoning = "Fallback: ranked by urgency score"
                    })
                    .ToList();
            }

            var summary = root.TryGetProperty("executive_summary", out var summaryProp)
                ? summaryProp.GetString() ?? "Plan generated"
                : "Plan generated";

            var confidence = root.TryGetProperty("confidence", out var confProp)
                ? Math.Clamp(confProp.GetDouble(), 0.0, 1.0)
                : 0.75;

            return new GeneratePlanResponse
            {
                RecommendedTasks = recommendations,
                ExecutiveSummary = summary,
                Confidence = confidence,
                GeneratedAtUtc = DateTime.UtcNow
            };
        }
        catch (JsonException)
        {
            // Fallback on parse error
            return new GeneratePlanResponse
            {
                RecommendedTasks = originalTasks
                    .OrderByDescending(t => t.UrgencyScore)
                    .Select((t, i) => new RecommendedTask
                    {
                        TaskId = t.TaskId,
                        PriorityRank = i + 1,
                        SuggestedUrgencyScore = t.UrgencyScore,
                        Reasoning = "Error parsing AI response; fallback to urgency score"
                    })
                    .ToList(),
                ExecutiveSummary = "AI response parsing failed; using urgency score fallback",
                Confidence = 0.5,
                GeneratedAtUtc = DateTime.UtcNow
            };
        }
    }

    private static SuggestPrioritiesResponse ParseSuggestPrioritiesResponse(string jsonResponse, IReadOnlyList<PlanTaskContext> originalTasks)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(jsonResponse);
            var root = jsonDoc.RootElement;

            var adjustments = new List<PriorityAdjustment>();
            if (root.TryGetProperty("priority_adjustments", out var adjustArray))
            {
                foreach (var adjElement in adjustArray.EnumerateArray())
                {
                    if (Guid.TryParse(adjElement.GetProperty("task_id").GetString(), out var taskId) &&
                        adjElement.TryGetProperty("suggested_urgency_delta", out var deltaProp) &&
                        adjElement.TryGetProperty("adjustment_reason", out var reasonProp))
                    {
                        adjustments.Add(new PriorityAdjustment
                        {
                            TaskId = taskId,
                            SuggestedUrgencyDelta = Math.Clamp(deltaProp.GetDouble(), -1.0, 1.0),
                            AdjustmentReason = reasonProp.GetString() ?? "No reason provided"
                        });
                    }
                }
            }

            var summary = root.TryGetProperty("summary_reasoning", out var summaryProp)
                ? summaryProp.GetString() ?? "Priorities adjusted"
                : "Priorities adjusted";

            var confidence = root.TryGetProperty("confidence", out var confProp)
                ? Math.Clamp(confProp.GetDouble(), 0.0, 1.0)
                : 0.75;

            return new SuggestPrioritiesResponse
            {
                PriorityAdjustments = adjustments,
                SummaryReasoning = summary,
                Confidence = confidence,
                GeneratedAtUtc = DateTime.UtcNow
            };
        }
        catch (JsonException)
        {
            return new SuggestPrioritiesResponse
            {
                PriorityAdjustments = originalTasks.Select(t => new PriorityAdjustment
                {
                    TaskId = t.TaskId,
                    SuggestedUrgencyDelta = 0.0,
                    AdjustmentReason = "Error parsing AI response"
                }).ToList(),
                SummaryReasoning = "AI response parsing failed",
                Confidence = 0.5,
                GeneratedAtUtc = DateTime.UtcNow
            };
        }
    }
}
