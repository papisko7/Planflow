using System.Text;
using PlanFlow.Benchmarks;
using PlanFlow.Benchmarks.Scenarios;

// Not top-level statements: top-level statements would synthesize a type named "Program" in
// THIS assembly too, colliding with PlanFlow.Api's own (explicit, public partial) "Program" type
// that WebApplicationFactory<Program> resolves by reflection. That collision made
// WebApplicationFactory re-invoke this benchmark runner's own entry point instead of the API's,
// recursing until it hit ASP.NET Core's 5-minute host-build timeout.
internal static class BenchmarkHost
{
    private static async Task Main()
    {
        var resultsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "results"));
        Directory.CreateDirectory(resultsDir);

        Console.WriteLine("PlanFlow Phase 5.2 Benchmarks");
        Console.WriteLine($"Results folder: {resultsDir}");
        Console.WriteLine();
        Console.WriteLine("Scenario C (SignalR real-time hub overhead) is skipped: no SignalR hub exists in this");
        Console.WriteLine("codebase yet (Chat is an [EXT]/optional feature per CLAUDE.md scope governance, not part");
        Console.WriteLine("of the mandatory ZAKRES PRACY scope) - there is nothing to benchmark. Documented as N/A");
        Console.WriteLine("rather than building a throwaway hub solely to produce a benchmark number.");

        // Both hosts are started up front and stopped only after both scenarios finish - see
        // BenchmarkWebApplicationFactory's remarks on why a dispose-then-start-a-new-host sequence
        // within one process corrupts Quartz's static logging bridge.
        var apiFactory = new BenchmarkWebApplicationFactory();
        var batchFactory = new BenchmarkWebApplicationFactory();
        await apiFactory.StartAsync();
        await batchFactory.StartAsync();

        var apiStats = await ApiLatencyScenario.RunAsync(apiFactory, resultsDir);
        var batchResults = await BatchJobScenario.RunAsync(batchFactory);

        await apiFactory.StopAsync();
        await batchFactory.StopAsync();

        var summaryPath = Path.Combine(resultsDir, "phase5.2-summary.md");
        var scenarioStats = apiStats.ScenarioStats[0];
        var p95 = scenarioStats.Ok.Latency.Percent95;
        var errorRate = scenarioStats.AllRequestCount == 0
            ? 0
            : 100.0 * scenarioStats.Fail.Request.Count / scenarioStats.AllRequestCount;

        var sb = new StringBuilder();
        sb.AppendLine("# Phase 5.2 Benchmark Results");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("## Scenario A — API Latency Under Load (GET /api/teams/{teamId}/tasks)");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value | SLA | Result |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine($"| Requests (ok / failed) | {scenarioStats.Ok.Request.Count} / {scenarioStats.Fail.Request.Count} | - | - |");
        sb.AppendLine($"| Throughput (RPS) | {scenarioStats.Ok.Request.RPS:F1} | - | - |");
        sb.AppendLine($"| Latency p50 | {scenarioStats.Ok.Latency.Percent50:F1} ms | - | - |");
        sb.AppendLine($"| Latency p95 | {p95:F1} ms | < 200 ms | {(p95 < 200 ? "PASS" : "FAIL")} |");
        sb.AppendLine($"| Latency p99 | {scenarioStats.Ok.Latency.Percent99:F1} ms | - | - |");
        sb.AppendLine($"| Error rate | {errorRate:F2}% | 0% | {(errorRate == 0 ? "PASS" : "FAIL")} |");
        sb.AppendLine();
        sb.AppendLine("Environment: TestServer in-memory transport (real JWT/RBAC/MediatR/EF Core pipeline, real");
        sb.AppendLine("Testcontainers PostgreSQL, in-memory distributed cache — no real Redis). 150 distinct");
        sb.AppendLine("simulated users (one JWT each) so the per-user rate limiter isn't what gets measured. This");
        sb.AppendLine("measures full application-layer latency, excluding OS TCP/socket overhead a real deployed");
        sb.AppendLine("Kestrel + network hop would add.");
        sb.AppendLine();
        sb.AppendLine("## Scenario B — PrioritizationJob Batch Throughput");
        sb.AppendLine();
        sb.AppendLine("| Dataset size | Elapsed | Tasks/sec | Allocated | Working-set delta |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var r in batchResults)
        {
            sb.AppendLine($"| {r.TaskCount} | {r.ElapsedMs:F1} ms | {r.TasksPerSecond:F1} | {r.AllocatedBytesDelta / 1024.0 / 1024.0:F2} MB | {r.WorkingSetDeltaBytes / 1024.0 / 1024.0:F2} MB |");
        }
        sb.AppendLine();
        sb.AppendLine("## Scenario C — SignalR Real-Time Hub Overhead");
        sb.AppendLine();
        sb.AppendLine("**N/A.** No SignalR hub is implemented in this codebase (Chat/real-time delivery is an [EXT],");
        sb.AppendLine("optional feature per Use_Case_Diagram.md, not part of the mandatory ZAKRES PRACY scope governed");
        sb.AppendLine("by CLAUDE.md). Skipped rather than building a throwaway hub solely to produce a benchmark number.");

        File.WriteAllText(summaryPath, sb.ToString());
        Console.WriteLine();
        Console.WriteLine($"Summary written to {summaryPath}");
    }
}
