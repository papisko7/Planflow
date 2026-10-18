namespace PlanFlow.Application.Common.Interfaces;

/// <summary>
/// A single normalized Google Calendar event, decoupled from Google's wire format so
/// SyncCalendarJob's mapping logic never touches JSON field names directly.
/// </summary>
/// <param name="Id">Google's event ID — the idempotency key SyncCalendarJob upserts <see cref="PlanFlow.Domain.Entities.CalendarItem"/> rows on.</param>
/// <param name="Title">Event summary/title; null for events with no title set.</param>
/// <param name="StartUtc">Event start (an all-day event starts at midnight UTC of its date); null only for malformed or cancelled events.</param>
/// <param name="EndUtc">Event end (an all-day event ends at midnight UTC of the day AFTER its last day, as Google sends it); null when Google sends none, which marks the imported item as incomplete.</param>
/// <param name="IsCancelled">True when the event's status is "cancelled" — Google's soft-delete signal.</param>
/// <param name="Description">The event description exactly as Google sends it (may be HTML); null when empty. Carries the optional <c>[planflow]</c> line.</param>
/// <param name="IsAllDay">True when Google sent bare dates (<c>start.date</c>) instead of date-times.</param>
/// <param name="EventType">Google's <c>eventType</c> as sent ("default", "outOfOffice", "focusTime", "workingLocation", "birthday", "fromGmail"); null when absent.</param>
/// <param name="ColorId">Google's <c>colorId</c> ("1"–"11"); null = the calendar's own colour.</param>
/// <param name="Location">Free-text place; for a working-location event, a label built from its type ("Home", "Office", or the custom label).</param>
/// <param name="IsFree">True when the event does not block time (<c>transparency: transparent</c>); false = busy, Google's default.</param>
/// <param name="RecurringEventId">Id of the series master when this is one occurrence of a repeating event (events are requested expanded).</param>
/// <param name="Status">Google's <c>status</c>: "confirmed", "tentative" or "cancelled"; null when absent.</param>
/// <param name="SettingsText">Read-only summary of Google-only settings (auto-decline, do-not-disturb, decline message); null when none.</param>
/// <param name="ReminderMinutes">
/// Minutes before the start for each reminder, with the calendar's default reminders already resolved when the event uses them.
/// Empty = no reminders; null = the caller did not say (the stored reminders are left alone).
/// </param>
public record GoogleCalendarEvent(
    string Id,
    string? Title,
    DateTime? StartUtc,
    DateTime? EndUtc,
    bool IsCancelled,
    string? Description = null,
    bool IsAllDay = false,
    string? EventType = null,
    string? ColorId = null,
    string? Location = null,
    bool IsFree = false,
    string? RecurringEventId = null,
    string? Status = null,
    string? SettingsText = null,
    IReadOnlyList<int>? ReminderMinutes = null);

/// <summary>
/// Talks to Google Calendar's REST API (events.list). Declared in Application so SyncCalendarJob
/// depends on an abstraction it can unit-test with a fake, mirroring <see cref="IGoogleOAuthClient"/>.
/// </summary>
public interface IGoogleCalendarClient
{
    /// <summary>How far ahead events are imported (also how far a repeating event is expanded).</summary>
    const int WindowDays = 90;

    /// <summary>
    /// Lists events on the user's primary calendar.
    /// </summary>
    /// <param name="accessToken">A valid (non-expired) OAuth access token for the calendar.readonly scope.</param>
    /// <param name="updatedSinceUtc">
    /// When set, only events changed since this timestamp are returned (Google's incremental-sync
    /// <c>updatedMin</c> parameter) — keeps a 5-minute poll cheap. When null (first sync, or the daily full pass),
    /// every event of the window is fetched, cancelled ones included. The window is always the same: from now to
    /// <see cref="WindowDays"/> days ahead, never the account's history, and its far end bounds the expansion of repeating
    /// events (a daily series would otherwise be endless). Either way every page of Google's answer is read.
    /// </param>
    Task<IReadOnlyList<GoogleCalendarEvent>> ListEventsAsync(string accessToken, DateTime? updatedSinceUtc, CancellationToken cancellationToken);
}
