using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Caching;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Notifications;
using PlanFlow.Application.Tasks.Common;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Polls every active Google Calendar connection every 5 minutes and upserts Google events into
/// <see cref="CalendarItem"/> rows of type Event (start–end), reading an optional <c>[planflow]</c> line for impact/effort and
/// flagging events that still lack data. Idempotency comes from the unique (SourceCalendarIntegrationId,
/// ExternalCalendarEventId) pair enforced in <c>CalendarItemConfiguration</c> — re-running the job
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
    private readonly INotificationService _notifications;
    private readonly ILogger<SyncCalendarJob> _logger;

    public SyncCalendarJob(
        IApplicationDbContext context,
        GoogleAccessTokenProvider accessTokenProvider,
        IGoogleCalendarClient calendarClient,
        ICacheService cache,
        INotificationService notifications,
        ILogger<SyncCalendarJob> logger)
    {
        _context = context;
        _accessTokenProvider = accessTokenProvider;
        _calendarClient = calendarClient;
        _cache = cache;
        _notifications = notifications;
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
            catch (UnauthorizedException ex)
            {
                // Google no longer accepts this connection (consent revoked, refresh token dead). Retrying every
                // 5 minutes would never succeed, so the connection is switched off and the user is e-mailed once
                // to reconnect (reconnecting reactivates the same row).
                failedAccounts++;
                _logger.LogWarning(ex, "Google rejected CalendarIntegration {IntegrationId} (user {UserId}); deactivating it.",
                    integration.Id, integration.UserId);
                await DeactivateAndNotifyAsync(integration, cancellationToken);
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
            "SyncCalendarJob run complete: {SyncedAccounts} account(s) synced, {FailedAccounts} failed, {Created} item(s) created, {Updated} item(s) updated.",
            syncedAccounts, failedAccounts, totalCreated, totalUpdated);
    }

    private async Task DeactivateAndNotifyAsync(CalendarIntegration integration, CancellationToken cancellationToken)
    {
        try
        {
            integration.IsActive = false;
            integration.UpdatedAtUtc = DateTime.UtcNow;
            await _notifications.NotifyAsync(
                NotificationEvent.CalendarConnectionLost,
                new[] { integration.UserId },
                EmailTemplates.CalendarConnectionLost(integration.ExternalAccountEmail),
                cancellationToken: cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not deactivate CalendarIntegration {IntegrationId}.", integration.Id);
        }
    }

    private async Task<(int Created, int Updated)> SyncIntegrationAsync(CalendarIntegration integration, CancellationToken cancellationToken)
    {
        // A synced task needs a TeamId (tasks are team-owned), but a calendar connection is
        // per-user. Calendar events are personal, so they go into the user's personal team;
        // accounts without one fall back to the earliest team they joined.
        var destinationTeamId = await _context.TeamMembers
            .Where(m => m.UserId == integration.UserId)
            .OrderByDescending(m => m.Team.IsPersonal)
            .ThenBy(m => m.JoinedAtUtc)
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
        var incompleteItems = new List<CalendarItem>(); // newly imported events that still need the user's details

        foreach (var calendarEvent in events)
        {
            var task = await _context.CalendarItems.FirstOrDefaultAsync(
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
                affectedTeamIds.Add(task.TeamId);
                updated++;
                continue;
            }

            if (calendarEvent.StartUtc is null)
            {
                // An event with no start cannot be placed on the timeline (Google always sends one for live events).
                _logger.LogWarning("Skipping Google event {EventId} of integration {IntegrationId}: it has no start time.",
                    calendarEvent.Id, integration.Id);
                continue;
            }

            // The optional "[planflow] impact=8 effort=2h" line (title or description) supplies data Google has no field for.
            var description = PlanflowTagParser.ToPlainText(calendarEvent.Description);
            var tag = PlanflowTagParser.Parse(calendarEvent.Title, description);
            var title = string.IsNullOrWhiteSpace(calendarEvent.Title) ? string.Empty : PlanflowTagParser.StripFromTitle(calendarEvent.Title);

            if (task is null)
            {
                // A calendar entry is a fixed-time happening, so it is imported as an Event (start–end), never as a Task.
                task = new CalendarItem
                {
                    TeamId = destinationTeamId.Value,
                    AssignedUserId = integration.UserId,
                    Title = title.Length == 0 ? "(untitled Google Calendar event)" : title,
                    Description = description,
                    Status = TaskStatus.Todo,
                    TaskType = TaskType.Event,
                    StartUtc = calendarEvent.StartUtc,
                    EndUtc = calendarEvent.EndUtc,
                    ImpactScore = tag?.ImpactScore,
                    EstimatedEffortHours = tag?.EffortHours,
                    SourceCalendarIntegrationId = integration.Id,
                    ExternalCalendarEventId = calendarEvent.Id
                };
                task.IsDataIncomplete = IsMissingData(task, nowUtc);
                _context.CalendarItems.Add(task);
                created++;

                if (task.IsDataIncomplete)
                {
                    incompleteItems.Add(task);
                }
            }
            else
            {
                task.Title = title.Length == 0 ? task.Title : title;
                task.Description = description;
                // Also converts rows imported before events existed (they were Tasks with a deadline) on their next change.
                task.TaskType = TaskType.Event;
                task.StartUtc = calendarEvent.StartUtc;
                task.EndUtc = calendarEvent.EndUtc;
                task.DeadlineUtc = null;
                // A tag in Google wins over what is stored; a missing tag never erases what the user filled in.
                task.ImpactScore = tag?.ImpactScore ?? task.ImpactScore;
                task.EstimatedEffortHours = tag?.EffortHours ?? task.EstimatedEffortHours;
                // The flag is raised once, at import. A later sync only lowers it (the data arrived), so an event the user
                // chose to leave without impact is not flagged and announced again after every edit in Google.
                if (task.IsDataIncomplete && !IsMissingData(task, nowUtc))
                {
                    task.IsDataIncomplete = false;
                }
                if (task.Status == TaskStatus.Cancelled)
                {
                    // Google un-cancelled a previously-cancelled event (e.g. the organizer restored it).
                    task.Status = TaskStatus.Todo;
                }
                task.UpdatedAtUtc = nowUtc;
                updated++;
            }

            affectedTeamIds.Add(task.TeamId);
        }

        if (incompleteItems.Count > 0)
        {
            // One message per run listing every newly imported event that needs details (inbox + push + e-mail by preference),
            // saved in the same transaction as the events themselves. A single event opens directly when tapped.
            await _notifications.NotifyAsync(
                NotificationEvent.CalendarItemsIncomplete,
                new[] { integration.UserId },
                EmailTemplates.CalendarItemsIncomplete(incompleteItems.Select(i => i.Title).ToList()),
                cancellationToken: cancellationToken,
                taskId: incompleteItems.Count == 1 ? incompleteItems[0].Id : null);
        }

        integration.LastSyncedAtUtc = nowUtc;
        integration.UpdatedAtUtc = nowUtc;

        await _context.SaveChangesAsync(cancellationToken);

        // A synced deadline change moves the heaviest-weighted urgency component, so each touched team is
        // rescored right away (not at the next hourly PrioritizationJob pass), after the new tasks exist in the DB.
        foreach (var teamId in affectedTeamIds)
        {
            var changedTaskIds = await TeamUrgencyRecalculator.RecalculateTeamAsync(
                _context, teamId, nowUtc, ScoreTriggerSource.CalendarSync, null, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await TeamUrgencyRecalculator.InvalidateCachesAsync(_cache, teamId, changedTaskIds, cancellationToken);
        }

        return (created, updated);
    }

    /// <summary>
    /// An imported event is "incomplete" when the ranking lacks data only the user can add: no impact (Google has no such field —
    /// it can come from the <c>[planflow]</c> tag) or no end time. An event that is already over is never flagged: nothing to rank.
    /// </summary>
    private static bool IsMissingData(CalendarItem item, DateTime nowUtc) =>
        (item.ImpactScore is null || item.EndUtc is null) && (item.EndUtc is null || item.EndUtc > nowUtc);
}
