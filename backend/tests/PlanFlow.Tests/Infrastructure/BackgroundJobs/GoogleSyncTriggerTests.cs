using System.Collections.Specialized;
using PlanFlow.Infrastructure.BackgroundJobs;
using Quartz;
using Quartz.Impl;
using Xunit;

namespace PlanFlow.Tests.Infrastructure.BackgroundJobs;

/// <summary>Step 7.5.9: the sync jobs have no timer. A press queues them for one user, and the status can tell that user's sync is still going.</summary>
public class GoogleSyncTriggerTests
{
    /// <summary>Stands in for the real sync: signals that it started and stays "running" until the test lets it go.</summary>
    public class GateJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            ((TaskCompletionSource)context.Scheduler.Context["started"]).TrySetResult();
            await ((TaskCompletionSource)context.Scheduler.Context["release"]).Task;
        }
    }

    private static async Task<IScheduler> CreateSchedulerAsync()
    {
        var properties = new NameValueCollection { ["quartz.scheduler.instanceName"] = Guid.NewGuid().ToString() };
        var scheduler = await new StdSchedulerFactory(properties).GetScheduler();
        // The same two durable, trigger-less jobs the application registers.
        foreach (var key in new[] { GoogleSyncTrigger.CalendarJob, GoogleSyncTrigger.TasksJob })
        {
            await scheduler.AddJob(JobBuilder.Create<GateJob>().WithIdentity(key).StoreDurably().Build(), replace: true);
        }
        return scheduler;
    }

    [Fact]
    public async Task NothingStarted_NothingIsRunning()
    {
        var scheduler = await CreateSchedulerAsync();
        try
        {
            Assert.False(await GoogleSyncTrigger.IsRunningAsync(scheduler, Guid.NewGuid(), CancellationToken.None));
            Assert.Empty(await scheduler.GetTriggersOfJob(GoogleSyncTrigger.CalendarJob)); // no timer: the job waits for a press
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Fact]
    public async Task Started_ShowsAsRunningForThatUserOnly_FromQueuedUntilFinished()
    {
        var scheduler = await CreateSchedulerAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Context["started"] = started;
        scheduler.Context["release"] = release;
        var user = Guid.NewGuid();
        try
        {
            // Queued, scheduler not started yet: still counts as running, so the app does not mistake the gap for "done".
            await GoogleSyncTrigger.StartAsync(scheduler, user, CancellationToken.None);
            Assert.True(await GoogleSyncTrigger.IsRunningAsync(scheduler, user, CancellationToken.None));
            Assert.False(await GoogleSyncTrigger.IsRunningAsync(scheduler, Guid.NewGuid(), CancellationToken.None));

            await scheduler.Start();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(await GoogleSyncTrigger.IsRunningAsync(scheduler, user, CancellationToken.None));
            Assert.False(await GoogleSyncTrigger.IsRunningAsync(scheduler, Guid.NewGuid(), CancellationToken.None));

            release.SetResult();
            await WaitUntilAsync(async () => !await GoogleSyncTrigger.IsRunningAsync(scheduler, user, CancellationToken.None));
        }
        finally
        {
            release.TrySetResult();
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Fact]
    public async Task TheRunCarriesTheUsersId()
    {
        var scheduler = await CreateSchedulerAsync();
        var user = Guid.NewGuid();
        try
        {
            await GoogleSyncTrigger.StartAsync(scheduler, user, CancellationToken.None);

            var triggers = await scheduler.GetTriggersOfJob(GoogleSyncTrigger.TasksJob);
            Assert.Equal(user.ToString(), Assert.Single(triggers).JobDataMap.GetString("UserId"));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not become true in time.");
            await Task.Delay(50);
        }
    }
}
