using Microsoft.Extensions.Configuration;
using PlanFlow.Application.AiPlanner.Common;

namespace PlanFlow.Infrastructure.AiClients;

/// <summary>
/// Factory for creating AI planner client instances based on configuration.
/// Supports providers: mock (default), claude, openai (structure).
/// Configuration via AI_PROVIDER environment variable or appsettings["AiPlanner:Provider"].
/// </summary>
public static class AiPlannerClientFactory
{
    public static IAiPlannerClient CreateClient(IConfiguration configuration, HttpClient? httpClient = null)
    {
        var provider = configuration["AiPlanner:Provider"]
            ?? Environment.GetEnvironmentVariable("AI_PROVIDER")
            ?? "mock";

        return provider.ToLowerInvariant() switch
        {
            "claude" => CreateClaudeClient(configuration, httpClient),
            "openai" => throw new NotImplementedException("OpenAI client not yet implemented"),
            "mock" or _ => new MockAiPlannerClient()
        };
    }

    private static IAiPlannerClient CreateClaudeClient(IConfiguration configuration, HttpClient? httpClient)
    {
        var apiKey = configuration["AiPlanner:Claude:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "Anthropic API key not configured. Set AiPlanner:Claude:ApiKey in appsettings or ANTHROPIC_API_KEY env var.");

        var model = configuration["AiPlanner:Claude:Model"] ?? "claude-3-5-sonnet-20241022";

        httpClient ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        return new ClaudeAiPlannerClient(httpClient, apiKey, model);
    }
}
