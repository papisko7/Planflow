using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Polls every active Google Calendar connection every 5 minutes and upserts Google events into
/// <see cref="TaskItem"/> rows. Idempotency comes from the unique (SourceCalendarIntegrationId,
/// ExternalCalendarEventId) pair enforced in <c>TaskItemConfiguration</c> — re-running the job
/// never creates duplicates, it just finds and updates the same row.
///
/// One user's Google account failing (revoked consent, Google outage) must not stop the other
/// accounts in the same run, so each integration is processed and committed independently inside
/// its own try/catch.
/// </summary>
public class SyncCalendarJob : IJob
{
    private readonly IApplicationDbContext _context;
    private readonly GoogleAccessTokenProvider _accessTokenProvider;
    private readonly IGoogleCalendarClient _calendarClient;
    private readonly ICacheService _cache;
    private readonly ILogger<SyncCalendarJob> _logger;

    public SyncCalendarJob(
        IApplicationDbContext context,
        GoogleAccessTokenProvider accessTokenProvider,
        IGoogleCalendarClient calendarClient,
        ICacheService cache,
        ILogger<SyncCalendarJob> logger)
    {
        _context = context;
        _accessTokenProvider = accessTokenProvider;
        _calendarClient = calendarClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;

        var integrations = await _context.CalendarIntegrations
            .Where(i => i.IsActive)
            .ToListAsync(cancellationToken);

        if (integrations.Count == 0)
        {
            return;
        }

        var syncedAccounts = 0;
        var failedAccounts = 0;
        var totalCreated = 0;
        var totalUpdated = 0;

        foreach (var integration in integrations)
        {
            try
            {
                var (created, updated) = await SyncIntegrationAsync(integration, cancellationToken);
                totalCreated += created;
                totalUpdated += updated;
                syncedAccounts++;
            }
            catch (Exception ex)
            {
                // A single account's failure (expired/revoked refresh token, Google outage, an
                // account with no team to sync into) is logged and skipped — it never aborts the
                // rest of this run.
                failedAccounts++;
                _logger.LogWarning(ex, "SyncCalendarJob failed for CalendarIntegration {IntegrationId} (user {UserId}); skipping this account for this run.",
                    integration.Id, integration.UserId);
            }
        }

        _logger.LogInformation(
            "SyncCalendarJob run complete: {SyncedAccounts} account(s) synced, {FailedAccounts} failed, {Created} task(s) created, {Updated} task(s) updated.",
            syncedAccounts, failedAccounts, totalCreated, totalUpdated);
    }

    private async Task<(int Created, int Updated)> SyncIntegrationAsync(CalendarIntegration integration, CancellationToken cancellationToken)
    {
        // A synced task needs a TeamId (tasks are team-owned), but a calendar connection is
        // per-user. We sync into the earliest team the user joined — the closest thing this
        // schema has to a "home" team for a personal calendar import.
        var destinationTeamId = await _context.TeamMembers
            .Where(m => m.UserId == integration.UserId)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => (Guid?)m.TeamId)
            .FirstOrDefaultAsync(cancellationToken);

        if (destinationTeamId is null)
        {
            _logger.LogWarning("Skipping CalendarIntegration {IntegrationId}: user {UserId} has no team to sync tasks into.",
                integration.Id, integration.UserId);
            return (0, 0);
        }

        var accessToken = await _accessTokenProvider.GetValidAccessTokenAsync(integration, cancellationToken);
        IReadOnlyList<GoogleCalendarEvent> events;
        try
        {
            events = await _calendarClient.ListEventsAsync(accessToken, integration.LastSyncedAtUtc, cancellationToken);
        }
        catch (UnauthorizedException)
        {
            // Google rejected the token even though our locally stored expiry said it was still
            // valid (e.g. consent was revoked and re-granted out of band). Force a refresh and
            // retry exactly once instead of failing this run and waiting for the next 5-minute
            // poll — a second UnauthorizedException here is a genuine failure and propagates to
            // the caller's per-integration catch block like any other.
            accessToken = await _accessTokenProvider.RefreshAccessTokenAsync(integration, cancellationToken);
            events = await _calendarClient.ListEventsAsync(accessToken, integration.LastSyncedAtUtc, cancellationToken);
        }

        var nowUtc = DateTime.UtcNow;
        var created = 0;
        var updated = 0;
        var affectedTeamIds = new HashSet<Guid>();

        foreach (var calendarEvent in events)
        {
            var task = await _context.Tasks.FirstOrDefaultAsync(
                t => t.SourceCalendarIntegrationId == integration.Id && t.ExternalCalendarEventId == calendarEvent.Id,
                cancellationToken);

            if (calendarEvent.IsCancelled)
            {
                if (task is null || task.Status == TaskStatus.Cancelled)
                {
                    continue; // nothing to cancel — either never imported, or already cancelled
                }

                task.Status = TaskStatus.Cancelled;
                task.UpdatedAtUtc = nowUtc;
                await RecalculateUrgencyAsync(task, nowUtc, cancellationToken);
                affectedTeamIds.Add(task.TeamId);
                updated++;
                continue;
            }

            var deadlineUtc = calendarEvent.EndUtc ?? calendarEvent.StartUtc;

            if (task is null)
            {
                task = new TaskItem
                {
                    TeamId = destinationTeamId.Value,
                    AssignedUserId = integration.UserId,
                    Title = string.IsNullOrWhiteSpace(calendarEvent.Title) ? "(untitled Google Calendar event)" : calendarEvent.Title,
                    Status = TaskStatus.Todo,
                    DeadlineUtc = deadlineUtc,
                    SourceCalendarIntegrationId = integration.Id,
                    ExternalCalendarEventId = calendarEvent.Id
                };
                _context.Tasks.Add(task);
                created++;
            }
            else
            {
                task.Title = string.IsNullOrWhiteSpace(calendarEvent.Title) ? task.Title : calendarEvent.Title;
                task.DeadlineUtc = deadlineUtc;
                if (task.Status == TaskStatus.Cancelled)
                {
                    // Google un-cancelled a previously-cancelled event (e.g. the organizer restored it).
                    task.Status = TaskStatus.Todo;
                }
                task.UpdatedAtUtc = nowUtc;
                updated++;
            }

            await RecalculateUrgencyAsync(task, nowUtc, cancellationToken);
            affectedTeamIds.Add(task.TeamId);
        }

        integration.LastSyncedAtUtc = nowUtc;
        integration.UpdatedAtUtc = nowUtc;

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var teamId in affectedTeamIds)
        {
            await _cache.RemoveAsync(CacheKeys.TeamTasks(teamId), cancellationToken);
        }

        return (created, updated);
    }

    /// <summary>
    /// A synced deadline change moves the heaviest-weighted urgency component, so the score (and
    /// its audit log) must be refreshed immediately rather than waiting for the next hourly
    /// PrioritizationJob pass.
    /// </summary>
    private async Task RecalculateUrgencyAsync(TaskItem task, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var blockedCount = await _context.Tasks.CountAsync(t => t.BlockedByTaskId == task.Id, cancellationToken);
        var scoreLog = TaskUrgencyScoreFactory.BuildScoreLog(task, blockedCount, nowUtc, ScoreTriggerSource.CalendarSync);

        task.CurrentUrgencyScore = scoreLog.FinalScore;
        _context.UrgencyScoreLogs.Add(scoreLog);
        await _cache.RemoveAsync(CacheKeys.TaskUrgency(task.Id), cancellationToken);
    }
}
