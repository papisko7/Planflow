namespace PlanFlow.Tests.Integration.PostgresIntegration;

/// <summary>
/// Shares one <see cref="PostgresWebApplicationFactory"/> (and its underlying Testcontainers
/// PostgreSQL instance) across every test class marked <c>[Collection(Name)]</c> below, since
/// starting a fresh container per test class would make the suite unnecessarily slow.
/// </summary>
[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresWebApplicationFactory>
{
    public const string Name = "Postgres";
}
