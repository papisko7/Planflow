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
/// Fetches the Google Tasks of the user who pressed "Sync now" (see <see cref="GoogleSyncTrigger"/>; there is no timer), provided the
/// account agreed to the tasks permission, and upserts them into <see cref="CalendarItem"/> rows of type Task. Idempotency comes from
/// the unique (SourceCalendarIntegrationId, ExternalTaskId) pair in <c>CalendarItemConfiguration</c>, so running it again finds and
/// updates the same row. Runs never overlap, so two presses cannot race for the same row.
///
/// Ownership mirrors the calendar import. What Google Tasks has (title, notes, due day, done / open) can be edited in the app as well, and the
/// sync goes both ways with "last write wins": a task that changed in Google since the last sync is taken over (see
/// <see cref="CalendarItem.ExternalFieldsHash"/>), an edit made in the app (see <see cref="CalendarItem.LocalEditedFields"/>) is sent to Google,
/// and when both happened to the same task the later change - Google's own update time against the time of the app's edit - replaces the other.
/// PlanFlow owns what Google Tasks has no field for (impact, effort, the planned start, blocked-by, its own status choices) and never sends it.
///
/// Mapping, in plain words: the <c>due</c> day becomes the deadline (a whole day, read literally); the planned start is the day the task
/// was first seen (or its due day when that is already behind us) and is never touched again; a task with no due day gets "tomorrow" and is
/// flagged as defaulted. A new task is "unverified" (<see cref="CalendarItem.IsDataIncomplete"/>) unless a <c>[planflow]</c> line already
/// gave both impact and effort, because Google has no such fields; saving the item in the app clears the flag too. To keep a first import
/// from burying the app in history, only tasks that are still open are imported; completed or deleted ones are followed only when already imported.
///
/// One account failing must not stop the others when a run covers several, so each is processed and committed on its own inside a
/// try/catch. A rejected token is only logged here: the calendar job owns deactivating a connection Google no longer accepts, and the
/// calendar may well still work.
/// </summary>
[DisallowConcurrentExecution]
public class SyncTasksJob : IJob
{
    // Column widths (see CalendarItemConfiguration / ExternalTaskListConfiguration); Google's text is cut to fit instead of failing the sync.
    private const int MaxTitleLength = 256;
    private const int MaxExternalStatusLength = 32;
    private const int MaxExternalIdLength = 512;

    // Google's task statuses as remembered in CalendarItem.ExternalStatus ("deleted" is not a Google status but a separate flag).
    private const string NeedsAction = "needsAction";
    private const string Completed = "completed";
    private const string Deleted = "deleted";

    private readonly IApplicationDbContext _context;
    private readonly GoogleAccessTokenProvider _accessTokenProvider;
    private readonly IGoogleTasksClient _tasksClient;
    private readonly ICacheService _cache;
    private readonly INotificationService _notifications;
    private readonly ILogger<SyncTasksJob> _logger;

    public SyncTasksJob(
        IApplicationDbContext context,
        GoogleAccessTokenProvider accessTokenProvider,
        IGoogleTasksClient tasksClient,
        ICacheService cache,
        INotificationService notifications,
        ILogger<SyncTasksJob> logger)
    {
        _context = context;
        _accessTokenProvider = accessTokenProvider;
        _tasksClient = tasksClient;
        _cache = cache;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var userId = GoogleSyncTrigger.UserIdOf(context);

        // An account connected before Google Tasks existed has no tasks permission yet; it is skipped until the user reconnects.
        // The base of both Tasks scopes matches the read-only and the full one.
        var integrations = await _context.CalendarIntegrations
            .Where(i => i.IsActive && i.GrantedScopes.Contains(CalendarIntegration.TasksScopeBase) && (userId == null || i.UserId == userId))
            .ToListAsync(cancellationToken);

        if (integrations.Count == 0)
        {
            return;
        }

        var syncedAccounts = 0;
        var failedAccounts = 0;
        var totalCreated = 0;
        var totalUpdated = 0;
        var totalSent = 0;

        foreach (var integration in integrations)
        {
            try
            {
                var (created, updated, sent) = await SyncIntegrationAsync(integration, cancellationToken);
                totalCreated += created;
                totalUpdated += updated;
                totalSent += sent;
                syncedAccounts++;
            }
            catch (Exception ex)
            {
                failedAccounts++;
                _logger.LogWarning(ex, "SyncTasksJob failed for CalendarIntegration {IntegrationId} (user {UserId}); skipping this account for this run.",
                    integration.Id, integration.UserId);
            }
        }

        _logger.LogInformation(
            "SyncTasksJob run complete: {SyncedAccounts} account(s) synced, {FailedAccounts} failed, {Created} task(s) created, {Updated} task(s) updated, {Sent} edit(s) sent to Google.",
            syncedAccounts, failedAccounts, totalCreated, totalUpdated, totalSent);
    }

    private async Task<(int Created, int Updated, int Sent)> SyncIntegrationAsync(CalendarIntegration integration, CancellationToken cancellationToken)
    {
        var destinationTeamId = await SyncDestination.FindTeamIdAsync(_context, integration.UserId, cancellationToken);
        if (destinationTeamId is null)
        {
            _logger.LogWarning("Skipping Google Tasks of integration {IntegrationId}: user {UserId} has no team to sync tasks into.",
                integration.Id, integration.UserId);
            return (0, 0, 0);
        }

        // Taken BEFORE asking Google: a task edited while the request is in flight is then newer than this moment and comes back next sync.
        var syncStartedAtUtc = DateTime.UtcNow;
        var updatedSinceUtc = integration.LastTasksSyncedAtUtc;

        // "Today" is the person's day, not the server's: it becomes the planned start of every task seen for the first time.
        var timeZoneId = await _context.Users
            .Where(u => u.Id == integration.UserId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        var today = AllDayTime.LiteralToday(syncStartedAtUtc, AllDayTime.ZoneOrUtc(timeZoneId));

        var googleLists = await _accessTokenProvider.CallAsync(
            integration, token => _tasksClient.ListTaskListsAsync(token, cancellationToken), cancellationToken);

        var storedLists = await _context.ExternalTaskLists
            .Where(l => l.CalendarIntegrationId == integration.Id)
            .ToListAsync(cancellationToken);

        // One query for everything already imported; new tasks are added to it so a task listed twice is never created twice.
        var known = await _context.CalendarItems
            .Where(i => i.SourceCalendarIntegrationId == integration.Id && i.ExternalTaskId != null)
            .ToDictionaryAsync(i => i.ExternalTaskId!, cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var created = 0;
        var updated = 0;
        var allListsRead = true;
        var incompleteItems = new List<CalendarItem>(); // newly imported tasks that still need the user's details
        var listed = new Dictionary<string, GoogleTask>(); // the tasks Google returned in this run, by id

        foreach (var googleList in googleLists)
        {
            var list = storedLists.FirstOrDefault(l => l.ExternalId == googleList.Id);

            IReadOnlyList<GoogleTask> tasks;
            try
            {
                // A list we have never seen is read in full: its tasks may be older than the last poll, so "changed since" would miss them.
                var since = list is null ? null : updatedSinceUtc;
                tasks = await _accessTokenProvider.CallAsync(
                    integration, token => _tasksClient.ListTasksAsync(token, googleList.Id, since, cancellationToken), cancellationToken);
            }
            catch (UnauthorizedException)
            {
                throw; // the whole connection is the problem, not this list
            }
            catch (Exception ex)
            {
                // The list row is not stored (and the watermark not advanced) until its tasks were read, so the next poll starts over for it.
                allListsRead = false;
                _logger.LogWarning(ex, "Could not read Google task list {ListId} of integration {IntegrationId}; skipping it for this run.",
                    googleList.Id, integration.Id);
                continue;
            }

            var title = Truncate(googleList.Title, MaxTitleLength) ?? "(untitled list)";
            if (list is null)
            {
                list = new ExternalTaskList
                {
                    CalendarIntegrationId = integration.Id,
                    ExternalId = Truncate(googleList.Id, MaxExternalIdLength)!,
                    Title = title
                };
                _context.ExternalTaskLists.Add(list);
                storedLists.Add(list);
            }
            else if (list.Title != title)
            {
                list.Title = title;
                list.UpdatedAtUtc = nowUtc;
            }

            foreach (var googleTask in tasks)
            {
                if (googleTask.IsSubtask)
                {
                    continue; // sub-tasks are not imported
                }

                listed[googleTask.Id] = googleTask;
                known.TryGetValue(googleTask.Id, out var item);
                if (item is null)
                {
                    item = CreateIfOpen(googleTask, integration, destinationTeamId.Value, list, today);
                    if (item is null)
                    {
                        continue; // history (done or deleted) is not imported
                    }

                    known[googleTask.Id] = item;
                    _context.CalendarItems.Add(item);
                    if (item.IsDataIncomplete)
                    {
                        incompleteItems.Add(item);
                    }
                    created++;
                }
                else if (Update(item, googleTask, list, nowUtc))
                {
                    updated++;
                }
            }
        }

        updated += RemoveVanishedLists(googleLists, storedLists, known.Values, nowUtc);

        var sent = await SendLocalEditsAsync(integration, known.Values, listed, storedLists, nowUtc, cancellationToken);

        if (incompleteItems.Count > 0)
        {
            await _notifications.NotifyAsync(
                NotificationEvent.CalendarItemsIncomplete,
                new[] { integration.UserId },
                EmailTemplates.GoogleTasksIncomplete(incompleteItems.Select(i => i.Title).ToList()),
                cancellationToken: cancellationToken,
                taskId: incompleteItems.Count == 1 ? incompleteItems[0].Id : null);
        }

        if (allListsRead)
        {
            integration.LastTasksSyncedAtUtc = syncStartedAtUtc;
        }
        integration.UpdatedAtUtc = nowUtc;

        await _context.SaveChangesAsync(cancellationToken);

        var changedTaskIds = await TeamUrgencyRecalculator.RecalculateTeamAsync(
            _context, destinationTeamId.Value, nowUtc, ScoreTriggerSource.CalendarSync, null, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await TeamUrgencyRecalculator.InvalidateCachesAsync(_cache, destinationTeamId.Value, changedTaskIds, cancellationToken);

        return (created, updated, sent);
    }

    /// <summary>
    /// The way back: sends to Google the edits the person made in the app and Google has not seen (those <see cref="Update"/> did not replace
    /// with a newer Google version). An edit leaves only if the account allowed PlanFlow to change its tasks; otherwise it waits. A failure of one
    /// task never stops the others: a change Google will not take is dropped (the edit then stays in the app only), a temporary failure leaves
    /// the edit waiting for the next sync. Returns how many tasks were changed in Google.
    /// </summary>
    private async Task<int> SendLocalEditsAsync(
        CalendarIntegration integration, IEnumerable<CalendarItem> items, IReadOnlyDictionary<string, GoogleTask> listed,
        IReadOnlyList<ExternalTaskList> lists, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var waiting = items.Where(i => i.LocalEditedFields != MirroredFields.None).ToList();
        if (waiting.Count == 0)
        {
            return 0;
        }

        if (!integration.CanWriteTasks)
        {
            _logger.LogInformation("{Count} edit(s) wait to be sent to Google: integration {IntegrationId} has not allowed PlanFlow to change its tasks yet.",
                waiting.Count, integration.Id);
            return 0;
        }

        var sent = 0;
        foreach (var item in waiting)
        {
            try
            {
                if (await SendOneAsync(integration, item, listed, lists, nowUtc, cancellationToken))
                {
                    sent++;
                }
            }
            catch (UnauthorizedException)
            {
                throw; // the whole connection is the problem, not this task
            }
            catch (GoogleWriteRejectedException ex) when (ex.Failure == GoogleWriteFailure.MissingPermission)
            {
                _logger.LogWarning("Google refused to let integration {IntegrationId} change its tasks; the remaining edits wait until the user reconnects.", integration.Id);
                break;
            }
            catch (GoogleWriteRejectedException ex) when (ex.Failure == GoogleWriteFailure.Gone)
            {
                CancelAsDeletedInGoogle(item, nowUtc);
            }
            catch (GoogleWriteRejectedException ex)
            {
                _logger.LogWarning(ex, "Google will not take the edit of task {TaskId} (integration {IntegrationId}); it stays in PlanFlow only.",
                    item.ExternalTaskId, integration.Id);
                GoogleMirror.Settle(item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send the edit of task {TaskId} (integration {IntegrationId}); it waits for the next sync.",
                    item.ExternalTaskId, integration.Id);
            }
        }

        return sent;
    }

    /// <summary>
    /// Sends one waiting edit. The task is read first when the list did not return it (it is unchanged in Google, but the text of its title and
    /// notes must be known to keep the <c>[planflow]</c> part). Afterwards the item and Google agree again, so the fingerprints are taken from
    /// the task as Google now holds it - the next sync then sees no change. False when nothing had to be sent.
    /// </summary>
    private async Task<bool> SendOneAsync(
        CalendarIntegration integration, CalendarItem item, IReadOnlyDictionary<string, GoogleTask> listed, IReadOnlyList<ExternalTaskList> lists,
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        var taskId = item.ExternalTaskId!;
        var listId = lists.FirstOrDefault(l => l.Id == item.ExternalTaskListId)?.ExternalId;
        if (listId is null)
        {
            GoogleMirror.Settle(item); // the list is gone from Google, so is the task
            return false;
        }

        var current = listed.TryGetValue(taskId, out var fromList)
            ? fromList
            : await _accessTokenProvider.CallAsync(integration, token => _tasksClient.GetTaskAsync(token, listId, taskId, cancellationToken), cancellationToken);
        if (current is null || current.IsDeleted)
        {
            CancelAsDeletedInGoogle(item, nowUtc);
            return false;
        }

        var patch = GoogleMirror.BuildTaskPatch(item, current);
        var after = patch is null
            ? current
            : await _accessTokenProvider.CallAsync(integration, token => _tasksClient.PatchTaskAsync(token, listId, taskId, patch, cancellationToken), cancellationToken);

        item.ExternalFieldsHash = FieldsHash(after);
        item.ExternalDescriptionHash = ExternalContentHash.Compute(after.Title, after.Notes);
        item.ExternalStatus = after.IsCompleted ? Completed : NeedsAction;
        GoogleMirror.Settle(item);
        item.UpdatedAtUtc = nowUtc;
        return patch is not null;
    }

    /// <summary>A deletion in Google is final: the task is cancelled here, never erased, and an edit waiting in the app has nothing left to be sent to.</summary>
    private static void CancelAsDeletedInGoogle(CalendarItem item, DateTime nowUtc)
    {
        item.Status = TaskStatus.Cancelled;
        item.ExternalStatus = Deleted;
        GoogleMirror.Settle(item);
        item.UpdatedAtUtc = nowUtc;
    }

    /// <summary>A task seen for the first time: imported only while it is still open, as a Backlog task that is unverified until impact and effort are known.</summary>
    private static CalendarItem? CreateIfOpen(
        GoogleTask googleTask, CalendarIntegration integration, Guid teamId, ExternalTaskList list, DateTime today)
    {
        if (googleTask.IsDeleted || googleTask.IsCompleted)
        {
            return null;
        }

        var content = ReadContent(googleTask);

        // Due day = deadline. Without one the task is due "tomorrow" so it is never overdue on arrival, and is marked as defaulted.
        var hasDue = googleTask.DueDay is not null;
        var deadline = googleTask.DueDay ?? today.AddDays(1);
        // Google Tasks has no start. The day we first saw the task stands in for it - unless the task is already overdue, since a task
        // cannot start after its deadline.
        var start = today < deadline ? today : deadline;

        var item = new CalendarItem
        {
            TeamId = teamId,
            AssignedUserId = integration.UserId,
            Title = Truncate(content.Title.Length == 0 ? "(untitled Google task)" : content.Title, MaxTitleLength)!,
            Description = content.Description,
            Status = TaskStatus.Backlog,
            ExternalStatus = NeedsAction,
            TaskType = TaskType.Task,
            IsAllDay = true,
            StartUtc = start,
            DeadlineUtc = deadline,
            IsDeadlineDefaulted = !hasDue,
            ImpactScore = content.Tag?.ImpactScore,
            EstimatedEffortHours = content.Tag?.EffortHours,
            ExternalDescriptionHash = content.Hash,
            ExternalFieldsHash = FieldsHash(googleTask),
            SourceCalendarIntegrationId = integration.Id,
            ExternalTaskId = Truncate(googleTask.Id, MaxExternalIdLength),
            ExternalTaskListId = list.Id,
            ExternalTaskList = list
        };
        item.IsDataIncomplete = IsMissingData(item);
        return item;
    }

    /// <summary>Google Tasks has neither impact nor effort: a task is unverified until a <c>[planflow]</c> line or the user supplies both.</summary>
    private static bool IsMissingData(CalendarItem item) => item.ImpactScore is null || item.EstimatedEffortHours is null;

    private static string FieldsHash(GoogleTask googleTask) =>
        ExternalContentHash.ComputeFields(googleTask.Title, googleTask.Notes, googleTask.DueDay);

    /// <summary>Brings an already imported task up to date with Google. Returns whether anything was touched.</summary>
    private static bool Update(CalendarItem item, GoogleTask googleTask, ExternalTaskList list, DateTime nowUtc)
    {
        var googleStatus = googleTask.IsDeleted ? Deleted : googleTask.IsCompleted ? Completed : NeedsAction;

        if (googleTask.IsDeleted)
        {
            // Removed in Google while we hold data of our own (alerts, notes...): it is cancelled, never erased. An edit waiting in the
            // app has nothing left to be sent to.
            if (item.Status == TaskStatus.Cancelled && item.ExternalStatus == Deleted)
            {
                GoogleMirror.Settle(item);
                return false;
            }

            CancelAsDeletedInGoogle(item, nowUtc);
            return true;
        }

        var content = ReadContent(googleTask);
        item.ExternalTaskListId = list.Id;
        item.ExternalTaskList = list;

        // Google changed the task when its title, notes or due day no longer match the fingerprint of the last sync, or its done / open state
        // moved. If the person also edited it in the app since, the later of the two changes wins (last write wins): Google's version replaces
        // the edit, or the edit stays (the fields in "kept") and is sent to Google after the loop.
        var fieldsHash = FieldsHash(googleTask);
        var fieldsChanged = item.ExternalFieldsHash != fieldsHash;
        var statusChanged = item.ExternalStatus != googleStatus;
        var kept = MirroredFields.None;
        if ((fieldsChanged || statusChanged) && item.LocalEditedFields != MirroredFields.None)
        {
            if (GoogleMirror.GoogleIsNewer(googleTask.UpdatedUtc, item.LocalEditedAtUtc))
            {
                // Google's version replaces the edit, a completion made in the app included.
                if (item.LocalEditedFields.HasFlag(MirroredFields.Completed) && (item.Status == TaskStatus.Completed) != (googleStatus == Completed))
                {
                    item.Status = googleStatus == Completed ? TaskStatus.Completed : TaskStatus.Backlog;
                }
                GoogleMirror.Settle(item);
            }
            else
            {
                kept = item.LocalEditedFields;
            }
        }

        if (fieldsChanged)
        {
            if (content.Title.Length > 0 && !kept.HasFlag(MirroredFields.Title))
            {
                item.Title = Truncate(content.Title, MaxTitleLength)!;
            }
            if (!kept.HasFlag(MirroredFields.Description))
            {
                item.Description = content.Description;
            }
            // Removing the due day in Google keeps the deadline we hold (a person's own choice must not be reset by a poll).
            if (googleTask.DueDay is { } due && !kept.HasFlag(MirroredFields.Due))
            {
                item.DeadlineUtc = due;
                item.IsDeadlineDefaulted = false;
            }
            item.ExternalFieldsHash = fieldsHash;
        }
        if (item.StartUtc is { } start && item.DeadlineUtc is { } deadline && start > deadline)
        {
            item.StartUtc = deadline;
        }

        // The [planflow] tag stays in Google for good; it is applied only when Google's text really changed, so it cannot keep
        // overwriting what the user has since typed into the app. A missing tag never erases anything.
        if (item.ExternalDescriptionHash != content.Hash)
        {
            item.ImpactScore = content.Tag?.ImpactScore ?? item.ImpactScore;
            item.EstimatedEffortHours = content.Tag?.EffortHours ?? item.EstimatedEffortHours;
            item.ExternalDescriptionHash = content.Hash;
        }

        // Raised once, at import; a later poll only lowers it, when the data arrived (e.g. a [planflow] line was added in Google).
        if (item.IsDataIncomplete && !IsMissingData(item))
        {
            item.IsDataIncomplete = false;
        }

        // Status follows Google only when Google's own status changed since the last poll, so a status chosen in the app
        // (e.g. In progress) is not undone by every unrelated edit. A completion made in the app that is newer than Google's change stays
        // and is sent to Google; Google's state is still noted as seen.
        if (statusChanged)
        {
            if (!kept.HasFlag(MirroredFields.Completed))
            {
                item.Status = googleStatus == Completed ? TaskStatus.Completed : TaskStatus.Backlog;
            }
            item.ExternalStatus = Truncate(googleStatus, MaxExternalStatusLength);
        }

        item.UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// A list that is gone from Google takes its still-open tasks with it: they are cancelled (never erased) and lose their group.
    /// Guarded by "Google listed at least one list", so an empty answer can never wipe everything.
    /// </summary>
    private int RemoveVanishedLists(
        IReadOnlyList<GoogleTaskList> googleLists, List<ExternalTaskList> storedLists, IEnumerable<CalendarItem> items, DateTime nowUtc)
    {
        if (googleLists.Count == 0)
        {
            return 0;
        }

        var cancelled = 0;
        var listed = googleLists.Select(l => l.Id).ToHashSet();
        foreach (var gone in storedLists.Where(l => !listed.Contains(l.ExternalId)).ToList())
        {
            foreach (var item in items.Where(i => i.ExternalTaskListId == gone.Id))
            {
                item.ExternalTaskListId = null;
                item.ExternalTaskList = null;
                if (!TaskStatusRules.IsFinished(item.Status))
                {
                    item.Status = TaskStatus.Cancelled;
                    item.ExternalStatus = Deleted;
                    item.UpdatedAtUtc = nowUtc;
                    cancelled++;
                }
            }

            _context.ExternalTaskLists.Remove(gone);
            storedLists.Remove(gone);
        }

        return cancelled;
    }

    /// <summary>The parts of a Google task after the <c>[planflow]</c> line has been read out and stripped.</summary>
    private readonly record struct Content(string Title, string? Description, PlanflowTag? Tag, string Hash);

    private static Content ReadContent(GoogleTask googleTask)
    {
        var plainNotes = PlanflowTagParser.ToPlainText(googleTask.Notes);
        var tag = PlanflowTagParser.Parse(googleTask.Title, plainNotes);
        var title = string.IsNullOrWhiteSpace(googleTask.Title) ? string.Empty : PlanflowTagParser.StripFromTitle(googleTask.Title);
        return new Content(
            title,
            PlanflowTagParser.StripTagLines(plainNotes),
            tag,
            ExternalContentHash.Compute(googleTask.Title, googleTask.Notes));
    }

    /// <summary>Empty becomes null; text longer than the column is cut instead of making the whole sync fail on save.</summary>
    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}
