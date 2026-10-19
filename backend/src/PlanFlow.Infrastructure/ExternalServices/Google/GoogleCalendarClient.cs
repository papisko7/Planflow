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
    private const int PageSize = 250; // Google's maximum
    private const int MaxPages = 100; // 25 000 events: only a guard against a token loop that never ends

    private readonly HttpClient _httpClient;

    public GoogleCalendarClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<GoogleCalendarEvent>> ListEventsAsync(string accessToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var events = new List<GoogleCalendarEvent>();
        string? pageToken = null;

        // Google answers in pages of at most 250 events and hands out a token for the next one. Reading only the first page
        // (as the first version did) silently dropped everything after it - with a daily repeating event in the calendar that
        // was most of the future.
        for (var page = 0; page < MaxPages; page++)
        {
            var body = await GetPageAsync(accessToken, BuildQuery(updatedSinceUtc, now, pageToken), cancellationToken);
            events.AddRange((body.Items ?? []).Select(ToEvent(body.DefaultReminders)));

            pageToken = body.NextPageToken;
            if (string.IsNullOrEmpty(pageToken))
            {
                return events;
            }
        }

        throw new InvalidOperationException($"Google Calendar returned more than {MaxPages} pages of events; refusing to read on.");
    }

    private static string BuildQuery(DateTime? updatedSinceUtc, DateTime now, string? pageToken)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["singleEvents"] = "true"; // expands recurring events into individual instances so each has its own ID/time
        query["maxResults"] = PageSize.ToString();

        // The same forward window for every request: never the account's history, and a bounded expansion of repeating events.
        query["timeMin"] = now.ToString("o");
        query["timeMax"] = now.AddDays(IGoogleCalendarClient.WindowDays).ToString("o");

        if (updatedSinceUtc is { } updatedSince)
        {
            // Incremental sync: only events Google has touched since our last poll (Google always includes the ones
            // deleted since then, whatever showDeleted says).
            query["updatedMin"] = updatedSince.ToString("o");
            query["orderBy"] = "updated";
        }
        else
        {
            // Full pass: also ask for deleted events, so an event removed in Google while we were not looking is still cancelled here.
            query["showDeleted"] = "true";
            query["orderBy"] = "startTime";
        }

        if (!string.IsNullOrEmpty(pageToken))
        {
            query["pageToken"] = pageToken;
        }

        return query.ToString()!;
    }

    private async Task<GoogleEventsResponse> GetPageAsync(string accessToken, string query, CancellationToken cancellationToken)
    {
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

        return await response.Content.ReadFromJsonAsync<GoogleEventsResponse>(cancellationToken)
            ?? throw new UnauthorizedException("Google returned an empty Calendar events response.");
    }

    private static Func<GoogleEventItem, GoogleCalendarEvent> ToEvent(List<GoogleReminderOverride>? calendarDefaults) => item =>
    {
        var isWorkingLocation = item.EventType == "workingLocation";
        return new GoogleCalendarEvent(
            item.Id,
            item.Summary,
            item.Start?.ToUtc(),
            item.End?.ToUtc(),
            item.Status == "cancelled",
            item.Description,
            IsAllDay: item.Start?.Date is not null,
            EventType: item.EventType,
            ColorId: item.ColorId,
            Location: isWorkingLocation ? WorkingLocationLabel(item.WorkingLocationProperties) ?? item.Location : item.Location,
            IsFree: item.Transparency == "transparent",
            RecurringEventId: item.RecurringEventId,
            Status: item.Status,
            SettingsText: SettingsText(item),
            ReminderMinutes: ReminderMinutes(item.Reminders, calendarDefaults),
            IsPrivate: item.Visibility is "private" or "confidential");
    };

    /// <summary>
    /// The reminders that apply to an event: its own list, or - when it says "use default" - the calendar's default list.
    /// Pop-up and e-mail both become plain minutes; other methods (SMS, long gone from Google) are ignored.
    /// </summary>
    private static List<int> ReminderMinutes(GoogleReminders? reminders, List<GoogleReminderOverride>? calendarDefaults)
    {
        var source = reminders is null ? null : reminders.UseDefault ? calendarDefaults : reminders.Overrides;
        return (source ?? [])
            .Where(r => r.Method is "popup" or "email")
            .Select(r => r.Minutes)
            .ToList();
    }

    /// <summary>"Home", "Office" or the custom label, so the app can show where a person works that day.</summary>
    private static string? WorkingLocationLabel(GoogleWorkingLocation? properties) => properties?.Type switch
    {
        "homeOffice" => "Home",
        "officeLocation" => string.IsNullOrWhiteSpace(properties.OfficeLocation?.Label) ? "Office" : properties.OfficeLocation.Label,
        "customLocation" => string.IsNullOrWhiteSpace(properties.CustomLocation?.Label) ? "Other" : properties.CustomLocation.Label,
        _ => null
    };

    /// <summary>
    /// What an Out of office / Focus time event does in Google (declines invitations, silences chat, auto-reply text). We cannot
    /// act on it - the integration is read-only - so it is kept as one readable line the app shows as information.
    /// </summary>
    private static string? SettingsText(GoogleEventItem item)
    {
        var settings = item.EventType switch
        {
            "outOfOffice" => item.OutOfOfficeProperties,
            "focusTime" => item.FocusTimeProperties,
            _ => null
        };
        if (settings is null)
        {
            return null;
        }

        var parts = new List<string>();
        var decline = settings.AutoDeclineMode switch
        {
            "declineAllConflictingInvitations" => "declines all conflicting invitations",
            "declineOnlyNewConflictingInvitations" => "declines new conflicting invitations",
            _ => null
        };
        if (decline is not null)
        {
            parts.Add($"Auto-decline: {decline}");
        }
        if (settings.ChatStatus == "doNotDisturb")
        {
            parts.Add("Do not disturb");
        }
        if (!string.IsNullOrWhiteSpace(settings.DeclineMessage))
        {
            parts.Add($"Message: \"{settings.DeclineMessage.Trim()}\"");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private record GoogleEventsResponse(
        [property: JsonPropertyName("items")] List<GoogleEventItem>? Items,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
        [property: JsonPropertyName("defaultReminders")] List<GoogleReminderOverride>? DefaultReminders);

    private record GoogleEventItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("start")] GoogleEventDateTime? Start,
        [property: JsonPropertyName("end")] GoogleEventDateTime? End,
        [property: JsonPropertyName("eventType")] string? EventType,
        [property: JsonPropertyName("colorId")] string? ColorId,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("transparency")] string? Transparency,
        [property: JsonPropertyName("visibility")] string? Visibility,
        [property: JsonPropertyName("recurringEventId")] string? RecurringEventId,
        [property: JsonPropertyName("reminders")] GoogleReminders? Reminders,
        [property: JsonPropertyName("outOfOfficeProperties")] GoogleTemplateSettings? OutOfOfficeProperties,
        [property: JsonPropertyName("focusTimeProperties")] GoogleTemplateSettings? FocusTimeProperties,
        [property: JsonPropertyName("workingLocationProperties")] GoogleWorkingLocation? WorkingLocationProperties);

    private record GoogleReminders(
        [property: JsonPropertyName("useDefault")] bool UseDefault,
        [property: JsonPropertyName("overrides")] List<GoogleReminderOverride>? Overrides);

    private record GoogleReminderOverride(
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("minutes")] int Minutes);

    private record GoogleTemplateSettings(
        [property: JsonPropertyName("autoDeclineMode")] string? AutoDeclineMode,
        [property: JsonPropertyName("declineMessage")] string? DeclineMessage,
        [property: JsonPropertyName("chatStatus")] string? ChatStatus);

    private record GoogleWorkingLocation(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("customLocation")] GoogleLabelled? CustomLocation,
        [property: JsonPropertyName("officeLocation")] GoogleLabelled? OfficeLocation);

    private record GoogleLabelled([property: JsonPropertyName("label")] string? Label);

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
