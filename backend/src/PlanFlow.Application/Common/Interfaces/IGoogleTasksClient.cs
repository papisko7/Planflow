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
public record GoogleTask(
    string Id,
    string? Title,
    string? Notes,
    DateTime? DueDay,
    bool IsCompleted,
    bool IsDeleted,
    bool IsSubtask = false);

/// <summary>
/// Talks to the Google Tasks REST API (tasklists.list, tasks.list). Declared in Application so SyncTasksJob depends on an abstraction
/// it can unit-test with a fake, like <see cref="IGoogleCalendarClient"/>.
/// </summary>
public interface IGoogleTasksClient
{
    /// <summary>All task lists of the account (every page).</summary>
    /// <param name="accessToken">A valid OAuth access token with the tasks.readonly scope.</param>
    Task<IReadOnlyList<GoogleTaskList>> ListTaskListsAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Tasks of one list (every page), completed, hidden and deleted ones included so a completion or deletion made in Google is seen.
    /// </summary>
    /// <param name="updatedSinceUtc">When set, only tasks changed since then (Google's <c>updatedMin</c>); null = the whole list.</param>
    Task<IReadOnlyList<GoogleTask>> ListTasksAsync(string accessToken, string taskListId, DateTime? updatedSinceUtc, CancellationToken cancellationToken);
}
