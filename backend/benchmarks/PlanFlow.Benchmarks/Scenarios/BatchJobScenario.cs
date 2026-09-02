using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PlanFlow.Infrastructure.BackgroundJobs;
using Quartz;

namespace PlanFlow.Benchmarks.Scenarios;

public record BatchJobResult(int TaskCount, double ElapsedMs, double TasksPerSecond, long AllocatedBytesDelta, long WorkingSetDeltaBytes);

/// <summary>
/// Scenario B: wall-clock scaling of <see cref="PrioritizationJob"/> (the Quartz job that recomputes
/// every non-terminal task's urgency score) across dataset sizes. Each size gets a fresh team so one
/// run's tasks never leak into the next size's dataset. <see cref="PrioritizationJob.Execute"/> only
/// reads <c>context.CancellationToken</c> off Quartz's <see cref="IJobExecutionContext"/>, so a bare
/// Moq stub of that one member is enough to invoke the real job logic outside a live Quartz scheduler.
/// </summary>
/// <remarks>
/// Takes its own dedicated <see cref="BenchmarkWebApplicationFactory"/> (a separate PostgreSQL
/// container from Scenario A's) rather than sharing one: <see cref="PrioritizationJob"/> recomputes
/// urgency for every non-terminal task in the WHOLE database (not scoped to one team), so sharing a
/// database with Scenario A's 200 seeded tasks would inflate every dataset-size measurement by a
/// constant, non-obvious offset. <c>Program.cs</c> starts both factories up front and stops both only
/// after both scenarios finish, rather than starting/stopping sequentially - see
/// <see cref="BenchmarkWebApplicationFactory"/>'s remarks on the Quartz static-logger race that a
/// dispose-then-start-a-new-host sequence within one process triggers.
/// </remarks>
public static class BatchJobScenario
{
    private static readonly int[] DatasetSizes = [100, 1_000, 10_000];

    public static async Task<List<BatchJobResult>> RunAsync(BenchmarkWebApplicationFactory factory)
    {
        var results = new List<BatchJobResult>();

        Console.WriteLine();
        Console.WriteLine("=== Scenario B: Batch Job Processing Throughput (PrioritizationJob) ===");

        foreach (var size in DatasetSizes)
        {
            var (teamId, _) = await Seeding.SeedTeamWithMemberAsync(factory.Services);
            await Seeding.SeedTasksAsync(factory.Services, teamId, size);

            using var scope = factory.Services.CreateScope();
            var job = ActivatorUtilities.CreateInstance<PrioritizationJob>(scope.ServiceProvider);

            var jobContextMock = new Mock<IJobExecutionContext>();
            jobContextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var workingSetBefore = Process.GetCurrentProcess().WorkingSet64;

            var stopwatch = Stopwatch.StartNew();
            await job.Execute(jobContextMock.Object);
            stopwatch.Stop();

            var allocatedDelta = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var workingSetDelta = Process.GetCurrentProcess().WorkingSet64 - workingSetBefore;

            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            var tasksPerSecond = size / stopwatch.Elapsed.TotalSeconds;

            var result = new BatchJobResult(size, elapsedMs, tasksPerSecond, allocatedDelta, workingSetDelta);
            results.Add(result);

            Console.WriteLine(
                $"N={size,6}: {elapsedMs,9:F1} ms | {tasksPerSecond,8:F1} tasks/sec | " +
                $"allocated {allocatedDelta / 1024.0 / 1024.0,7:F2} MB | working-set delta {workingSetDelta / 1024.0 / 1024.0,7:F2} MB");
        }

        return results;
    }
}
