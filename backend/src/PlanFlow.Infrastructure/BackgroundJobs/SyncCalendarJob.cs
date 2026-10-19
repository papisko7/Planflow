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
using PlanFlow.Domain.Services;
using Quartz;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Fetches the Google Calendar of the user who pressed "Sync now" (see <see cref="GoogleSyncTrigger"/>; there is no timer) and upserts
/// Google events into <see cref="CalendarItem"/> rows of type Event (start–end), reading an optional <c>[planflow]</c> line for
/// impact/effort and flagging events that still lack data. Idempotency comes from the unique (SourceCalendarIntegrationId,
/// ExternalCalendarEventId) pair enforced in <c>CalendarItemConfiguration</c> — running it again
/// never creates duplicates, it just finds and updates the same row. Runs never overlap, so two presses cannot race for the same row.
///
/// Google owns the fields it has (title, time, place, colour, template, status, reminders, description); PlanFlow owns what Google
/// has no field for (impact, effort, blocked-by, the item's own status choices). A sync therefore overwrites the first group and
/// never touches the second, except through the <c>[planflow]</c> tag when its text really changed.
///
/// Most syncs are incremental (only events changed since the last one). A press more than a day after the last full pass gets a full
/// pass over the whole 90-day window instead: an incremental sync cannot see an event that merely drifts into the window as time passes,
/// and the full pass also repairs anything a sync missed.
///
/// One account failing (revoked consent, Google outage) must not stop the others when a run covers several, so each integration is
/// processed and committed independently inside its own try/catch.
/// </summary>
[DisallowConcurrentExecution]
public class SyncCalendarJob : IJob
{
    /// <summary>How long after the last full pass the next sync becomes a full pass again.</summary>
    private static readonly TimeSpan FullPassInterval = TimeSpan.FromHours(24);

    // Column widths (see CalendarItemConfiguration); Google's text is cut to fit instead of failing the whole sync.
    private const int MaxTitleLength = 256;
    private const int MaxLocationLength = 512;
    private const int MaxSeriesIdLength = 512;
    private const int MaxExternalStatusLength = 32;
    private const int MaxExternalSettingsLength = 1000;

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
        var userId = GoogleSyncTrigger.UserIdOf(context);

        var integrations = await _context.CalendarIntegrations
            .Where(i => i.IsActive && (userId == null || i.UserId == userId))
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
                // Google no longer accepts this connection (consent revoked, refresh token dead). Pressing Sync now
                // again would never succeed, so the connection is switched off and the user is e-mailed once
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
        // Calendar events are personal, so they go into the user's personal team (see SyncDestination).
        var destinationTeamId = await SyncDestination.FindTeamIdAsync(_context, integration.UserId, cancellationToken);

        if (destinationTeamId is null)
        {
            _logger.LogWarning("Skipping CalendarIntegration {IntegrationId}: user {UserId} has no team to sync tasks into.",
                integration.Id, integration.UserId);
            return (0, 0);
        }

        // Taken BEFORE asking Google: an event edited while the request is in flight is then newer than this moment and
        // comes back in the next sync, instead of falling into the gap between "fetched" and "now".
        var syncStartedAtUtc = DateTime.UtcNow;
        var fullPass = integration.LastSyncedAtUtc is null
            || integration.LastFullSyncAtUtc is null
            || syncStartedAtUtc - integration.LastFullSyncAtUtc >= FullPassInterval;
        var updatedSinceUtc = fullPass ? null : integration.LastSyncedAtUtc;

        var accessToken = await _accessTokenProvider.GetValidAccessTokenAsync(integration, cancellationToken);
        IReadOnlyList<GoogleCalendarEvent> events;
        try
        {
            events = await _calendarClient.ListEventsAsync(accessToken, updatedSinceUtc, cancellationToken);
        }
        catch (UnauthorizedException)
        {
            // Google rejected the token even though our locally stored expiry said it was still
            // valid (e.g. consent was revoked and re-granted out of band). Force a refresh and
            // retry exactly once instead of failing this run and making the user press the button
            // again — a second UnauthorizedException here is a genuine failure and propagates to
            // the caller's per-integration catch block like any other.
            accessToken = await _accessTokenProvider.RefreshAccessTokenAsync(integration, cancellationToken);
            events = await _calendarClient.ListEventsAsync(accessToken, updatedSinceUtc, cancellationToken);
        }

        var nowUtc = DateTime.UtcNow;
        var created = 0;
        var updated = 0;
        var affectedTeamIds = new HashSet<Guid>();
        var incompleteItems = new List<CalendarItem>(); // newly imported events that still need the user's details

        foreach (var calendarEvent in events)
        {
            var task = await _context.CalendarItems
                .Include(t => t.Reminders)
                .FirstOrDefaultAsync(
                    t => t.SourceCalendarIntegrationId == integration.Id && t.ExternalCalendarEventId == calendarEvent.Id,
                    cancellationToken);

            if (calendarEvent.IsCancelled)
            {
                if (task is null || task.Status == TaskStatus.Cancelled)
                {
                    continue; // nothing to cancel — either never imported, or already cancelled
                }

                task.Status = TaskStatus.Cancelled;
                task.ExternalStatus = "cancelled";
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

            // What the item's template allows (place, colour, reminders, description...) decides what is imported, so an
            // imported item never carries data its own form could not show or edit.
            var template = EventTemplateRules.FromGoogleEventType(calendarEvent.EventType);
            var rules = EventTemplateRules.For(TaskType.Event, template);
            var isInformational = rules.Profile == ScoringProfile.Informational;

            // The optional "[planflow] impact=8 effort=2h" line (title or description) supplies data Google has no field for.
            // Informational templates (Out of office, ...) have no impact/effort, so a tag on them is ignored.
            var plainDescription = PlanflowTagParser.ToPlainText(calendarEvent.Description);
            var tag = isInformational ? null : PlanflowTagParser.Parse(calendarEvent.Title, plainDescription);
            var title = string.IsNullOrWhiteSpace(calendarEvent.Title) ? string.Empty : PlanflowTagParser.StripFromTitle(calendarEvent.Title);
            var contentHash = ExternalContentHash.Compute(calendarEvent.Title, calendarEvent.Description);

            var googleStatus = string.IsNullOrWhiteSpace(calendarEvent.Status) ? "confirmed" : calendarEvent.Status;
            var description = rules.AllowsDescription ? PlanflowTagParser.StripTagLines(plainDescription) : null;
            var location = rules.Location == LocationMode.None ? null : Truncate(calendarEvent.Location?.Trim(), MaxLocationLength);
            var colorKey = rules.AllowsColor ? ItemColors.FromGoogleColorId(calendarEvent.ColorId) : null;
            var isBusy = rules.Busy != BusyMode.Choice || !calendarEvent.IsFree; // only a plain event can be "free"

            if (task is null)
            {
                // A calendar entry is a fixed-time happening, so it is imported as an Event (start–end), never as a Task.
                var fallbackTitle = rules.DefaultTitle ?? "(untitled Google Calendar event)";
                task = new CalendarItem
                {
                    TeamId = destinationTeamId.Value,
                    AssignedUserId = integration.UserId,
                    Title = Truncate(title.Length == 0 ? fallbackTitle : title, MaxTitleLength)!,
                    Description = description,
                    Status = StatusFromGoogle(googleStatus),
                    ExternalStatus = Truncate(googleStatus, MaxExternalStatusLength),
                    TaskType = TaskType.Event,
                    EventTemplate = template,
                    StartUtc = calendarEvent.StartUtc,
                    EndUtc = calendarEvent.EndUtc,
                    IsAllDay = calendarEvent.IsAllDay,
                    Location = location,
                    ColorKey = colorKey,
                    IsBusy = isBusy,
                    IsPrivate = calendarEvent.IsPrivate,
                    SeriesId = Truncate(calendarEvent.RecurringEventId, MaxSeriesIdLength),
                    ExternalSettings = Truncate(calendarEvent.SettingsText, MaxExternalSettingsLength),
                    ImpactScore = tag?.ImpactScore,
                    EstimatedEffortHours = tag?.EffortHours,
                    ExternalDescriptionHash = contentHash,
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
                task.Title = title.Length == 0 ? task.Title : Truncate(title, MaxTitleLength)!;
                task.Description = description;
                // Also converts rows imported before events existed (they were Tasks with a deadline) on their next change.
                task.TaskType = TaskType.Event;
                task.EventTemplate = template;
                task.StartUtc = calendarEvent.StartUtc;
                task.EndUtc = calendarEvent.EndUtc;
                task.DeadlineUtc = null;
                task.IsAllDay = calendarEvent.IsAllDay;
                task.Location = location;
                task.ColorKey = colorKey;
                task.IsBusy = isBusy;
                task.IsPrivate = calendarEvent.IsPrivate;
                task.SeriesId = Truncate(calendarEvent.RecurringEventId, MaxSeriesIdLength);
                task.ExternalSettings = Truncate(calendarEvent.SettingsText, MaxExternalSettingsLength);

                // The tag stays in Google for good, so reading it on every sync would keep overwriting what the user has since
                // typed into the app. It is applied only when Google's text is not what we saw last time. A tag in Google then
                // wins over what is stored; a missing tag never erases what the user filled in.
                if (task.ExternalDescriptionHash != contentHash)
                {
                    task.ImpactScore = tag?.ImpactScore ?? task.ImpactScore;
                    task.EstimatedEffortHours = tag?.EffortHours ?? task.EstimatedEffortHours;
                    task.ExternalDescriptionHash = contentHash;
                }

                // Status follows Google only when Google's status itself changed since the last sync, so a status the user chose
                // in the app (e.g. cancelling an event they will not attend) is not undone by every unrelated edit in Google.
                if (task.ExternalStatus != googleStatus)
                {
                    task.Status = StatusFromGoogle(googleStatus);
                    task.ExternalStatus = Truncate(googleStatus, MaxExternalStatusLength);
                }

                // The flag is raised once, at import. A later sync only lowers it (the data arrived), so an event the user
                // chose to leave without impact is not flagged and announced again after every edit in Google.
                if (task.IsDataIncomplete && !IsMissingData(task, nowUtc))
                {
                    task.IsDataIncomplete = false;
                }
                task.UpdatedAtUtc = nowUtc;
                updated++;
            }

            // Reminders belong to Google for an imported event: its list replaces ours whenever it differs. A template without
            // alerts (Out of office, Working location) keeps none. A caller that sent no list leaves the stored ones alone.
            IReadOnlyList<ItemReminder>? reminders = !rules.AllowsAlerts
                ? []
                : calendarEvent.ReminderMinutes is null ? null : GoogleReminderMapper.Map(calendarEvent.ReminderMinutes, calendarEvent.IsAllDay);
            if (reminders is not null)
            {
                ReplaceRemindersIfChanged(task, reminders);
            }

            affectedTeamIds.Add(task.TeamId);
        }

        if (fullPass)
        {
            await RemoveItemsBeyondWindowAsync(integration, syncStartedAtUtc, affectedTeamIds, cancellationToken);
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

        integration.LastSyncedAtUtc = syncStartedAtUtc;
        if (fullPass)
        {
            integration.LastFullSyncAtUtc = syncStartedAtUtc;
        }
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
    /// Clean-up of the daily full pass: an imported item that starts after the far end of the import window is deleted. Before the window
    /// existed (step 7.5.2) Google expanded a yearly event (a birthday) up to 2099 and every copy was stored; they would otherwise clutter
    /// every list for good. Google stays the source of truth: an item returns on its own when its day comes into the window.
    /// </summary>
    private async Task RemoveItemsBeyondWindowAsync(
        CalendarIntegration integration, DateTime syncStartedAtUtc, HashSet<Guid> affectedTeamIds, CancellationToken cancellationToken)
    {
        var windowEndUtc = syncStartedAtUtc.AddDays(IGoogleCalendarClient.WindowDays);
        // An older row can still be a Task with a deadline instead of an Event with a start (see the conversion above).
        var candidates = await _context.CalendarItems
            .Where(t => t.SourceCalendarIntegrationId == integration.Id && t.ExternalCalendarEventId != null && (t.StartUtc ?? t.DeadlineUtc) > windowEndUtc)
            .ToListAsync(cancellationToken);
        // The query sees the stored dates; an item this very pass moved into the window (Google changed its date) is tracked with the new
        // one and must stay, so the check is repeated on the values as they are now.
        var beyondWindow = candidates.Where(t => (t.StartUtc ?? t.DeadlineUtc) > windowEndUtc).ToList();
        if (beyondWindow.Count == 0)
        {
            return;
        }

        _context.CalendarItems.RemoveRange(beyondWindow);
        foreach (var teamId in beyondWindow.Select(t => t.TeamId))
        {
            affectedTeamIds.Add(teamId);
        }

        _logger.LogInformation("Removed {Count} imported items of integration {IntegrationId} that start beyond the {Days}-day window.",
            beyondWindow.Count, integration.Id, IGoogleCalendarClient.WindowDays);
    }

    /// <summary>
    /// Makes the item's reminders equal to Google's list, touching only what differs (the common case: nothing to write). A reminder that
    /// stays keeps its row, and with it the memory of having already gone off, so an unrelated change in Google does not send it again.
    /// </summary>
    private void ReplaceRemindersIfChanged(CalendarItem item, IReadOnlyList<ItemReminder> fromGoogle)
    {
        var wanted = fromGoogle.Select(r => r.Key).ToHashSet();
        var stale = item.Reminders.Where(r => !wanted.Contains(r.Key)).ToList();
        var storedKeys = item.Reminders.Select(r => r.Key).ToHashSet();
        var missing = fromGoogle.Where(r => !storedKeys.Contains(r.Key)).ToList();
        if (stale.Count == 0 && missing.Count == 0)
        {
            return;
        }

        _context.ItemReminders.RemoveRange(stale);
        foreach (var reminder in missing)
        {
            reminder.CalendarItemId = item.Id;
            _context.ItemReminders.Add(reminder);
        }
    }

    /// <summary>Google's "tentative" is our Tentative; everything else that is not cancelled is Confirmed.</summary>
    private static TaskStatus StatusFromGoogle(string googleStatus) =>
        googleStatus == "tentative" ? TaskStatus.Tentative : TaskStatus.Confirmed;

    /// <summary>Empty becomes null; text longer than the column is cut instead of making the whole sync fail on save.</summary>
    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>
    /// An imported event is "incomplete" when the ranking lacks data only the user can add: no impact (Google has no such field —
    /// it can come from the <c>[planflow]</c> tag) or no end time. An event that is already over is never flagged: nothing to rank.
    /// Informational templates (Out of office, Focus time, ...) are never ranked, so they are never incomplete.
    /// </summary>
    private static bool IsMissingData(CalendarItem item, DateTime nowUtc) =>
        EventTemplateRules.ProfileOf(item) == ScoringProfile.Full
        && (item.ImpactScore is null || item.EndUtc is null) && (item.EndUtc is null || item.EndUtc > nowUtc);
}
