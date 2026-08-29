using Microsoft.Extensions.Configuration;
using PlanFlow.Application.AiPlanner.Common;
using PlanFlow.Infrastructure.AiClients;
using Xunit;

namespace PlanFlow.Tests.Infrastructure.AiClients;

public class AiPlannerClientFactoryTests
{
    [Fact]
    public void CreateClient_WithMockProvider_ReturnsMockClient()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiPlanner:Provider"] = "mock" })
            .Build();

        // Act
        var client = AiPlannerClientFactory.CreateClient(config);

        // Assert
        Assert.IsType<MockAiPlannerClient>(client);
    }

    [Fact]
    public void CreateClient_WithNoProviderConfigured_DefaultsToMock()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        // Act
        var client = AiPlannerClientFactory.CreateClient(config);

        // Assert
        Assert.IsType<MockAiPlannerClient>(client);
    }

    [Fact]
    public void CreateClient_WithEnvironmentVariableMock_ReturnsMockClient()
    {
        // Arrange
        var originalEnv = Environment.GetEnvironmentVariable("AI_PROVIDER");
        Environment.SetEnvironmentVariable("AI_PROVIDER", "mock");
        var config = new ConfigurationBuilder().Build();

        try
        {
            // Act
            var client = AiPlannerClientFactory.CreateClient(config);

            // Assert
            Assert.IsType<MockAiPlannerClient>(client);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_PROVIDER", originalEnv);
        }
    }

    [Fact]
    public void CreateClient_WithClaudeProvider_MissingApiKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var originalEnv = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiPlanner:Provider"] = "claude" })
            .Build();

        try
        {
            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                AiPlannerClientFactory.CreateClient(config));
            Assert.Contains("Anthropic API key", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalEnv);
        }
    }

    [Fact]
    public void CreateClient_WithClaudeProvider_ValidApiKey_ReturnsClaudeClient()
    {
        // Arrange
        var originalEnv = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-test-key-123456789");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiPlanner:Provider"] = "claude" })
            .Build();

        try
        {
            // Act
            var client = AiPlannerClientFactory.CreateClient(config);

            // Assert
            Assert.IsType<ClaudeAiPlannerClient>(client);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalEnv);
        }
    }

    [Fact]
    public void CreateClient_WithOpenaiProvider_ThrowsNotImplementedException()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiPlanner:Provider"] = "openai" })
            .Build();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => AiPlannerClientFactory.CreateClient(config));
    }

    [Fact]
    public void CreateClient_WithInvalidProvider_DefaultsToMock()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiPlanner:Provider"] = "unknown-provider" })
            .Build();

        // Act
        var client = AiPlannerClientFactory.CreateClient(config);

        // Assert
        Assert.IsType<MockAiPlannerClient>(client);
    }
}
