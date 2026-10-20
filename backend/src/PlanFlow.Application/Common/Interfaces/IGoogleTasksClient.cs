namespace PlanFlow.Application.Common.Interfaces;

/// <summary>One Google Tasks list ("My Tasks", ...).</summary>
public record GoogleTaskList(string Id, string Title);

/// <summary>
/// A single normalized Google task, decoupled from Google's wire format so SyncTasksJob never touches JSON field names.
/// </summary>
/// <param name="Id">Google's task id: the key SyncTasksJob upserts on.</param>
/// <param name="Title">The task's title; null/empty when the user left it blank.</param>
/// <param name="Notes">The task's notes ("Details" in Google's UI), exactly as sent; carries the optional <c>[planflow]</c> line.</param>
/// <param name="DueDay">
/// The day the task is due, as midnight UTC of that calendar day (Google sends a date with a meaningless <c>00:00:00Z</c> time, so the date part is
/// read literally); null when the task has no due date.
/// </param>
/// <param name="IsCompleted">Google's <c>status = completed</c>.</param>
/// <param name="IsDeleted">True when the task was deleted in Google (only reported when deleted tasks are requested).</param>
/// <param name="IsSubtask">True when the task has a parent: sub-tasks are not imported.</param>
/// <param name="UpdatedUtc">Google's own <c>updated</c> time: when the task was last changed there, by anyone. The "updated at" of Google's version, compared with the app's when both changed it; null when Google sent none.</param>
public record GoogleTask(
    string Id,
    string? Title,
    string? Notes,
    DateTime? DueDay,
    bool IsCompleted,
    bool IsDeleted,
    bool IsSubtask = false,
    DateTime? UpdatedUtc = null);

/// <summary>
/// What to change in a Google task; a null member leaves that part as it is. Text members use an empty string for "clear it".
/// </summary>
/// <param name="Title">The whole title as Google should hold it (an optional <c>[planflow]</c> part included).</param>
/// <param name="Notes">The whole notes as Google should hold them, plain text.</param>
/// <param name="DueDay">The due day as midnight UTC of that calendar day.</param>
/// <param name="IsCompleted">True = done, false = open again.</param>
public record GoogleTaskPatch(string? Title = null, string? Notes = null, DateTime? DueDay = null, bool? IsCompleted = null)
{
    /// <summary>True when the patch changes nothing.</summary>
    public bool IsEmpty => Title is null && Notes is null && DueDay is null && IsCompleted is null;
}

/// <summary>
/// Talks to the Google Tasks REST API (tasklists.list, tasks.list / get / patch). Declared in Application so SyncTasksJob depends on an
/// abstraction it can unit-test with a fake, like <see cref="IGoogleCalendarClient"/>.
/// </summary>
public interface IGoogleTasksClient
{
    /// <summary>All task lists of the account (every page).</summary>
    /// <param name="accessToken">A valid OAuth access token with the tasks scope.</param>
    Task<IReadOnlyList<GoogleTaskList>> ListTaskListsAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Tasks of one list (every page), completed, hidden and deleted ones included so a completion or deletion made in Google is seen.
    /// </summary>
    /// <param name="updatedSinceUtc">When set, only tasks changed since then (Google's <c>updatedMin</c>); null = the whole list.</param>
    Task<IReadOnlyList<GoogleTask>> ListTasksAsync(string accessToken, string taskListId, DateTime? updatedSinceUtc, CancellationToken cancellationToken);

    /// <summary>One task as Google holds it now. Null when the task no longer exists; a deleted one that Google still reports comes back with <see cref="GoogleTask.IsDeleted"/> set.</summary>
    Task<GoogleTask?> GetTaskAsync(string accessToken, string taskListId, string taskId, CancellationToken cancellationToken);

    /// <summary>Changes the given parts of one task and returns it as Google now holds it.</summary>
    /// <exception cref="PlanFlow.Application.Common.Exceptions.GoogleWriteRejectedException">Google will not take the change.</exception>
    Task<GoogleTask> PatchTaskAsync(string accessToken, string taskListId, string taskId, GoogleTaskPatch patch, CancellationToken cancellationToken);
}
