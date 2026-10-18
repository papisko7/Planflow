using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Web;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Infrastructure.ExternalServices.Google;

/// <summary>
/// Raw HttpClient calls to the Google Calendar REST API — same minimal-dependency convention as
/// <see cref="GoogleOAuthClient"/> (no Google.Apis SDK).
/// </summary>
public class GoogleCalendarClient : IGoogleCalendarClient
{
    private const string EventsEndpoint = "https://www.googleapis.com/calendar/v3/calendars/primary/events";

    private readonly HttpClient _httpClient;

    public GoogleCalendarClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<GoogleCalendarEvent>> ListEventsAsync(string accessToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["singleEvents"] = "true"; // expands recurring events into individual instances so each has its own ID/time
        query["maxResults"] = "250";

        if (updatedSinceUtc is { } updatedSince)
        {
            // Incremental sync: only events Google has touched since our last successful poll.
            query["updatedMin"] = updatedSince.ToString("o");
            query["orderBy"] = "updated";
        }
        else
        {
            // First sync for this integration: only forward-looking events, never the account's
            // entire history, to keep the initial import bounded.
            query["timeMin"] = DateTime.UtcNow.ToString("o");
            query["orderBy"] = "startTime";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{EventsEndpoint}?{query}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Distinct from other failures so SyncCalendarJob can force a token refresh and retry
            // once, instead of just waiting out the resilience handler's retry budget below.
            throw new UnauthorizedException("Google rejected the Calendar events request (expired/revoked access token).");
        }

        // Any other failure (429 rate limit, 5xx, or a transport-level timeout as HttpRequestException)
        // is left to throw HttpRequestException via EnsureSuccessStatusCode, which the resilience
        // handler registered in DependencyInjection retries with exponential backoff before it ever
        // reaches SyncCalendarJob's per-integration catch block.
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GoogleEventsResponse>(cancellationToken)
            ?? throw new UnauthorizedException("Google returned an empty Calendar events response.");

        return (body.Items ?? [])
            .Select(item => new GoogleCalendarEvent(
                item.Id,
                item.Summary,
                item.Start?.ToUtc(),
                item.End?.ToUtc(),
                item.Status == "cancelled",
                item.Description))
            .ToList();
    }

    private record GoogleEventsResponse(
        [property: JsonPropertyName("items")] List<GoogleEventItem>? Items);

    private record GoogleEventItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("start")] GoogleEventDateTime? Start,
        [property: JsonPropertyName("end")] GoogleEventDateTime? End);

    // Timed events carry an offset ("...T15:00:00+02:00"); all-day events carry a bare date ("2026-10-06").
    // Npgsql only accepts Kind=Utc for timestamptz, so both shapes are normalised to UTC here.
    private record GoogleEventDateTime(
        [property: JsonPropertyName("dateTime")] DateTimeOffset? DateTimeValue,
        [property: JsonPropertyName("date")] DateTime? Date)
    {
        public DateTime? ToUtc() =>
            DateTimeValue?.UtcDateTime
            ?? (Date is { } date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null);
    }
}
