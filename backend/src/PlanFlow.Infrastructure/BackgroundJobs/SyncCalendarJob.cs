using Microsoft.Extensions.Logging;
using Quartz;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Polls Google Calendar for connected users and syncs event changes into PlanFlow.
/// Skeleton only: the actual Google Calendar client/OAuth flow is scoped to Phase 3
/// ("Google Calendar API Sync"); this job just establishes the scheduled entry point
/// so Phase 3 can fill in <see cref="Execute"/> without touching the scheduler wiring.
/// </summary>
public class SyncCalendarJob : IJob
{
    private readonly ILogger<SyncCalendarJob> _logger;

    public SyncCalendarJob(ILogger<SyncCalendarJob> logger)
    {
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        // TODO (Phase 3): fetch CalendarIntegration rows due for sync, call the Google Calendar
        // client, and upsert TaskItem deadlines/events from the returned changes.
        _logger.LogInformation("SyncCalendarJob tick (skeleton — Phase 3 wires the actual Google Calendar client).");
        return Task.CompletedTask;
    }
}
