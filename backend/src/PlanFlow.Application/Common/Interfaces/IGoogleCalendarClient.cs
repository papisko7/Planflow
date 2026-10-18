namespace PlanFlow.Application.Common.Interfaces;

/// <summary>
/// A single normalized Google Calendar event, decoupled from Google's wire format so
/// SyncCalendarJob's mapping logic never touches JSON field names directly.
/// </summary>
/// <param name="Id">Google's event ID — the idempotency key SyncCalendarJob upserts <see cref="PlanFlow.Domain.Entities.CalendarItem"/> rows on.</param>
/// <param name="Title">Event summary/title; null for events with no title set.</param>
/// <param name="StartUtc">Event start (an all-day event starts at midnight UTC of its date); null only for malformed or cancelled events.</param>
/// <param name="EndUtc">Event end; null when Google sends none, which marks the imported item as incomplete.</param>
/// <param name="IsCancelled">True when the event's status is "cancelled" — Google's soft-delete signal.</param>
/// <param name="Description">The event description exactly as Google sends it (may be HTML); null when empty. Carries the optional <c>[planflow]</c> line.</param>
public record GoogleCalendarEvent(string Id, string? Title, DateTime? StartUtc, DateTime? EndUtc, bool IsCancelled, string? Description = null);

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
