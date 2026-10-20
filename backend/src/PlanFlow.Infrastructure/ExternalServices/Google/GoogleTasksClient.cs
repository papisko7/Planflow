using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Web;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Infrastructure.ExternalServices.Google;

/// <summary>
/// Raw HttpClient calls to the Google Tasks REST API — same minimal-dependency convention as <see cref="GoogleCalendarClient"/>.
/// Lists task lists and tasks, reads one task and changes one (tasks.patch).
/// </summary>
public class GoogleTasksClient : IGoogleTasksClient
{
    private const string BaseUrl = "https://tasks.googleapis.com/tasks/v1";
    private const int PageSize = 100; // Google's maximum for both endpoints
    private const int MaxPages = 100; // only a guard against a page token that never ends

    private readonly HttpClient _httpClient;

    public GoogleTasksClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<GoogleTaskList>> ListTaskListsAsync(string accessToken, CancellationToken cancellationToken)
    {
        var lists = new List<GoogleTaskList>();
        string? pageToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["maxResults"] = PageSize.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(pageToken))
            {
                query["pageToken"] = pageToken;
            }

            var body = await GetAsync<ListsResponse>(accessToken, $"{BaseUrl}/users/@me/lists?{query}", cancellationToken);
            lists.AddRange((body.Items ?? []).Where(l => !string.IsNullOrEmpty(l.Id)).Select(l => new GoogleTaskList(l.Id!, l.Title ?? string.Empty)));

            pageToken = body.NextPageToken;
            if (string.IsNullOrEmpty(pageToken))
            {
                return lists;
            }
        }

        throw new InvalidOperationException($"Google Tasks returned more than {MaxPages} pages of lists; refusing to read on.");
    }

    public async Task<IReadOnlyList<GoogleTask>> ListTasksAsync(
        string accessToken, string taskListId, DateTime? updatedSinceUtc, CancellationToken cancellationToken)
    {
        var tasks = new List<GoogleTask>();
        string? pageToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["maxResults"] = PageSize.ToString(CultureInfo.InvariantCulture);
            // A completion or deletion made in Google must reach a task we already imported, so those are requested too;
            // the job decides which ones are worth importing for the first time.
            query["showCompleted"] = "true";
            query["showHidden"] = "true";
            query["showDeleted"] = "true";
            if (updatedSinceUtc is { } updatedSince)
            {
                query["updatedMin"] = updatedSince.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            }
            if (!string.IsNullOrEmpty(pageToken))
            {
                query["pageToken"] = pageToken;
            }

            var url = $"{BaseUrl}/lists/{Uri.EscapeDataString(taskListId)}/tasks?{query}";
            var body = await GetAsync<TasksResponse>(accessToken, url, cancellationToken);
            tasks.AddRange((body.Items ?? []).Where(t => !string.IsNullOrEmpty(t.Id)).Select(ToTask));

            pageToken = body.NextPageToken;
            if (string.IsNullOrEmpty(pageToken))
            {
                return tasks;
            }
        }

        throw new InvalidOperationException($"Google Tasks returned more than {MaxPages} pages of tasks; refusing to read on.");
    }

    public async Task<GoogleTask?> GetTaskAsync(string accessToken, string taskListId, string taskId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, TaskUrl(taskListId, taskId), accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return null;
        }

        await GoogleWriteErrors.EnsureAcceptedAsync(response, cancellationToken);
        return await ReadTaskAsync(response, cancellationToken);
    }

    public async Task<GoogleTask> PatchTaskAsync(
        string accessToken, string taskListId, string taskId, GoogleTaskPatch patch, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Patch, TaskUrl(taskListId, taskId), accessToken);
        request.Content = new StringContent(PatchBody(patch).ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GoogleWriteErrors.EnsureAcceptedAsync(response, cancellationToken);
        return await ReadTaskAsync(response, cancellationToken);
    }

    /// <summary>
    /// The body of tasks.patch. Only what the patch names is sent. The due day goes out as Google writes it (midnight UTC, the time carries no
    /// meaning); reopening a task also clears its completion time, which Google would otherwise keep.
    /// </summary>
    internal static JsonObject PatchBody(GoogleTaskPatch patch)
    {
        var body = new JsonObject();
        if (patch.Title is not null)
        {
            body["title"] = patch.Title;
        }
        if (patch.Notes is not null)
        {
            body["notes"] = patch.Notes;
        }
        if (patch.DueDay is { } due)
        {
            body["due"] = due.Date.ToString("yyyy-MM-dd'T'00:00:00.000'Z'", CultureInfo.InvariantCulture);
        }
        if (patch.IsCompleted is { } completed)
        {
            body["status"] = completed ? "completed" : "needsAction";
            if (!completed)
            {
                body["completed"] = null;
            }
        }

        return body;
    }

    private static string TaskUrl(string taskListId, string taskId) =>
        $"{BaseUrl}/lists/{Uri.EscapeDataString(taskListId)}/tasks/{Uri.EscapeDataString(taskId)}";

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task<GoogleTask> ReadTaskAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var item = await response.Content.ReadFromJsonAsync<TaskItem>(cancellationToken)
            ?? throw new InvalidOperationException("Google returned an empty task.");
        return ToTask(item);
    }

    private async Task<T> GetAsync<T>(string accessToken, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedException("Google rejected the Tasks request (expired/revoked access token).");
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException("Google returned an empty Tasks response.");
    }

    private static GoogleTask ToTask(TaskItem item) => new(
        item.Id!,
        item.Title,
        item.Notes,
        DueDay(item.Due),
        IsCompleted: item.Status == "completed",
        IsDeleted: item.Deleted == true,
        IsSubtask: !string.IsNullOrEmpty(item.Parent),
        UpdatedUtc: item.Updated?.UtcDateTime);

    /// <summary>
    /// Google's <c>due</c> is an RFC 3339 timestamp whose time part is always <c>00:00:00Z</c> and carries no meaning: the task is due on
    /// that DATE. The date is therefore read straight from the first ten characters, never converted to another time zone (which would
    /// show the previous day to anyone west of UTC).
    /// </summary>
    public static DateTime? DueDay(string? due) =>
        due is { Length: >= 10 } && DateTime.TryParseExact(due[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? DateTime.SpecifyKind(day, DateTimeKind.Utc)
            : null;

    private record ListsResponse(
        [property: JsonPropertyName("items")] List<TaskListItem>? Items,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken);

    private record TaskListItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title);

    private record TasksResponse(
        [property: JsonPropertyName("items")] List<TaskItem>? Items,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken);

    private record TaskItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("notes")] string? Notes,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("due")] string? Due,
        [property: JsonPropertyName("deleted")] bool? Deleted,
        [property: JsonPropertyName("parent")] string? Parent,
        [property: JsonPropertyName("updated")] DateTimeOffset? Updated);
}
