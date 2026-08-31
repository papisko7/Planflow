namespace PlanFlow.Application.Common.Interfaces;

/// <summary>
/// A single normalized Google Calendar event, decoupled from Google's wire format so
/// SyncCalendarJob's mapping logic never touches JSON field names directly.
/// </summary>
/// <param name="Id">Google's event ID — the idempotency key SyncCalendarJob upserts <see cref="PlanFlow.Domain.Entities.TaskItem"/> rows on.</param>
/// <param name="Title">Event summary/title; null for events with no title set.</param>
/// <param name="StartUtc">Event start; null for an all-day event with no timed start.</param>
/// <param name="EndUtc">Event end, used as the mapped task's deadline (falls back to <see cref="StartUtc"/> when absent).</param>
/// <param name="IsCancelled">True when the event's status is "cancelled" — Google's soft-delete signal.</param>
public record GoogleCalendarEvent(string Id, string? Title, DateTime? StartUtc, DateTime? EndUtc, bool IsCancelled);

/// <summary>
/// Talks to Google Calendar's REST API (events.list). Declared in Application so SyncCalendarJob
/// depends on an abstraction it can unit-test with a fake, mirroring <see cref="IGoogleOAuthClient"/>.
/// </summary>
public interface IGoogleCalendarClient
{
    /// <summary>
    /// Lists events on the user's primary calendar.
    /// </summary>
    /// <param name="accessToken">A valid (non-expired) OAuth access token for the calendar.readonly scope.</param>
    /// <param name="updatedSinceUtc">
    /// When set, only events changed since this timestamp are returned (Google's incremental-sync
    /// <c>updatedMin</c> parameter) — keeps a 5-minute poll cheap. When null (first sync for this
    /// integration), only events starting from now onward are fetched instead, so a brand-new
    /// connection doesn't import someone's entire event history.
    /// </param>
    Task<IReadOnlyList<GoogleCalendarEvent>> ListEventsAsync(string accessToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken);
}
