using Quartz;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// How a "Sync now" press reaches the two Google sync jobs. They have no timer: they are stored without a trigger and started here, each
/// time for one user (the user's id travels in the job data). A job started without a user, as the tests do, syncs every account.
/// </summary>
public static class GoogleSyncTrigger
{
    public static readonly JobKey CalendarJob = new("SyncCalendarJob");
    public static readonly JobKey TasksJob = new("SyncTasksJob");

    private const string UserIdKey = "UserId";

    /// <summary>Starts both jobs for one user's account.</summary>
    public static async Task StartAsync(IScheduler scheduler, Guid userId, CancellationToken cancellationToken)
    {
        foreach (var jobKey in new[] { CalendarJob, TasksJob })
        {
            await scheduler.TriggerJob(jobKey, new JobDataMap { [UserIdKey] = userId.ToString() }, cancellationToken);
        }
    }

    /// <summary>Whose account this run is for; null means every account.</summary>
    public static Guid? UserIdOf(IJobExecutionContext context) =>
        Guid.TryParse(UserIdIn(context.MergedJobDataMap), out var userId) ? userId : null;

    /// <summary>
    /// True while a sync of this user's account is queued or running. Both are checked: a job that was just started is a pending trigger
    /// for a moment before it shows up as executing, and the app must not see that gap as "finished".
    /// </summary>
    public static async Task<bool> IsRunningAsync(IScheduler scheduler, Guid userId, CancellationToken cancellationToken)
    {
        var wanted = userId.ToString();

        var running = await scheduler.GetCurrentlyExecutingJobs(cancellationToken);
        if (running.Any(c => IsGoogleSync(c.JobDetail.Key) && UserIdIn(c.MergedJobDataMap) == wanted))
        {
            return true;
        }

        foreach (var jobKey in new[] { CalendarJob, TasksJob })
        {
            var pending = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);
            if (pending.Any(t => UserIdIn(t.JobDataMap) == wanted))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGoogleSync(JobKey key) => key.Equals(CalendarJob) || key.Equals(TasksJob);

    // JobDataMap.GetString throws when the key is missing, and a run started without a user has no such key.
    private static string? UserIdIn(JobDataMap data) => data.TryGetValue(UserIdKey, out var value) ? value as string : null;
}
