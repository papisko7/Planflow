using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace PlanFlow.Benchmarks.Scenarios;

/// <summary>
/// Scenario A: latency/throughput of <c>GET /api/teams/{teamId}/tasks</c> (the team task-list/dashboard
/// endpoint — the closest real route to the "dashboard" the task brief names; there is no separate
/// <c>/api/tasks/dashboard</c> route in the current API surface) under concurrent load.
/// Runs against <see cref="BenchmarkWebApplicationFactory"/>'s TestServer transport: real JWT auth,
/// RBAC policy, MediatR handler, EF Core query and JSON serialization all execute for real against a
/// real Testcontainers PostgreSQL instance — the only thing NOT exercised is the OS TCP/socket stack,
/// since TestServer short-circuits actual network I/O. That's called out explicitly in the results
/// report rather than presented as a raw production number.
/// </summary>
public static class ApiLatencyScenario
{
    private const double P95TargetMs = 200.0;
    private const int VirtualUserCount = 150;

    public static async Task<NodeStats> RunAsync(BenchmarkWebApplicationFactory factory, string reportFolder)
    {
        var (teamId, ownerId) = await Seeding.SeedTeamWithMemberAsync(factory.Services);
        await Seeding.SeedTasksAsync(factory.Services, teamId, count: 200);

        // One user (and one rate-limit bucket, see RateLimitingConfig's per-user 100 req/min global
        // limiter) per simulated virtual user, so 100 req/sec of *distinct users* polling the
        // dashboard is what gets measured, not one user tripping their own rate limit.
        var tokens = await Seeding.SeedUsersAndMintTokensAsync(factory.Services, teamId, VirtualUserCount);

        var httpClient = factory.CreateClient();

        var scenario = Scenario.Create("dashboard_api_latency", async context =>
        {
            var token = tokens[Random.Shared.Next(tokens.Count)];
            var request = Http.CreateRequest("GET", $"api/teams/{teamId}/tasks")
                .WithHeader("Authorization", $"Bearer {token}");

            var response = await Http.Send(httpClient, request);
            return response;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // Smoke: prove the scenario/auth/route wiring works before committing to load.
            Simulation.Inject(rate: 1, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(5)),
            // Load: 100 requests/sec (roughly 100 concurrent VUs at ~1 req/sec each) for 30s.
            Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder(reportFolder)
            .WithReportFormats(ReportFormat.Html, ReportFormat.Csv, ReportFormat.Md)
            .Run();

        var scenarioStats = stats.ScenarioStats[0];
        var okStats = scenarioStats.Ok.Request;
        var p95 = scenarioStats.Ok.Latency.Percent95;
        var errorRate = scenarioStats.AllRequestCount == 0
            ? 0
            : 100.0 * scenarioStats.Fail.Request.Count / scenarioStats.AllRequestCount;

        Console.WriteLine();
        Console.WriteLine("=== Scenario A: API Latency Under Load ===");
        Console.WriteLine($"Requests: {scenarioStats.AllRequestCount} (ok: {okStats.Count}, failed: {scenarioStats.Fail.Request.Count})");
        Console.WriteLine($"Throughput (RPS): {scenarioStats.Ok.Request.RPS:F1}");
        Console.WriteLine($"Latency p50: {scenarioStats.Ok.Latency.Percent50:F1} ms");
        Console.WriteLine($"Latency p95: {p95:F1} ms (SLA target: < {P95TargetMs} ms) -> {(p95 < P95TargetMs ? "PASS" : "FAIL")}");
        Console.WriteLine($"Latency p99: {scenarioStats.Ok.Latency.Percent99:F1} ms");
        Console.WriteLine($"Error rate: {errorRate:F2}% (SLA target: 0%) -> {(errorRate == 0 ? "PASS" : "FAIL")}");

        return stats;
    }
}
