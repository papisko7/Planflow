using System.Net;
using System.Web;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.ExternalServices.Google;

namespace PlanFlow.Tests.Integration;

/// <summary>
/// The real GoogleCalendarClient reading real-shaped JSON from a stubbed transport: what is asked of Google (pages, time window,
/// incremental vs. full) and how each field of an event is understood.
/// </summary>
public class GoogleCalendarClientTests
{
    private static GoogleCalendarClient ClientOver(StubHttpMessageHandler handler) => new(new HttpClient(handler));

    private static string Page(string items, string? nextPageToken = null, string? defaultReminders = null) =>
        "{" +
        (nextPageToken is null ? "" : $"\"nextPageToken\": \"{nextPageToken}\",") +
        (defaultReminders is null ? "" : $"\"defaultReminders\": {defaultReminders},") +
        $"\"items\": [{items}]}}";

    private static string TimedEvent(string id, string extra = "") =>
        $$"""
        {"id": "{{id}}", "summary": "Event {{id}}", "status": "confirmed",
         "start": {"dateTime": "2026-10-20T10:00:00+02:00"}, "end": {"dateTime": "2026-10-20T11:00:00+02:00"} {{extra}}}
        """;

    private static async Task<GoogleCalendarEvent> OneEvent(string eventJson, string? defaultReminders = null)
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, Page(eventJson, defaultReminders: defaultReminders));
        return Assert.Single(await ClientOver(handler).ListEventsAsync("token", null, CancellationToken.None));
    }

    private static System.Collections.Specialized.NameValueCollection QueryOf(HttpRequestMessage request) =>
        HttpUtility.ParseQueryString(request.RequestUri!.Query);

    // ---------- what we ask Google for ----------

    [Fact]
    public async Task EveryPage_IsRead_AndTheTokenOfThePreviousOneIsSent()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, Page(TimedEvent("a") + "," + TimedEvent("b"), nextPageToken: "page-2"))
            .Enqueue(HttpStatusCode.OK, Page(TimedEvent("c"), nextPageToken: "page-3"))
            .Enqueue(HttpStatusCode.OK, Page(TimedEvent("d")));

        var events = await ClientOver(handler).ListEventsAsync("token", null, CancellationToken.None);

        Assert.Equal(["a", "b", "c", "d"], events.Select(e => e.Id));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Null(QueryOf(handler.Requests[0])["pageToken"]);
        Assert.Equal("page-2", QueryOf(handler.Requests[1])["pageToken"]);
        Assert.Equal("page-3", QueryOf(handler.Requests[2])["pageToken"]);
    }

    [Fact]
    public async Task Pagination_ThatNeverEnds_StopsWithAnErrorInsteadOfLoopingForever()
    {
        var handler = new StubHttpMessageHandler();
        for (var i = 0; i < 101; i++)
        {
            handler.Enqueue(HttpStatusCode.OK, Page(TimedEvent($"e{i}"), nextPageToken: $"t{i}"));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => ClientOver(handler).ListEventsAsync("token", null, CancellationToken.None));
        Assert.Equal(100, handler.Requests.Count);
    }

    [Fact]
    public async Task FullPass_AsksForTheNinetyDayWindowAndForDeletedEvents()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, Page(""));
        var before = DateTime.UtcNow;

        await ClientOver(handler).ListEventsAsync("token", updatedSinceUtc: null, CancellationToken.None);

        var query = QueryOf(handler.Requests.Single());
        var timeMin = DateTime.Parse(query["timeMin"]!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var timeMax = DateTime.Parse(query["timeMax"]!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.InRange(timeMin, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.InRange((timeMax - timeMin).TotalDays, IGoogleCalendarClient.WindowDays - 0.01, IGoogleCalendarClient.WindowDays + 0.01);
        Assert.Equal("true", query["singleEvents"]);
        Assert.Equal("250", query["maxResults"]);
        Assert.Equal("true", query["showDeleted"]);
        Assert.Equal("startTime", query["orderBy"]);
        Assert.Null(query["updatedMin"]);
    }

    [Fact]
    public async Task IncrementalPoll_AsksOnlyForChanges_InsideTheSameWindow()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, Page(""));
        var since = new DateTime(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc);

        await ClientOver(handler).ListEventsAsync("token", since, CancellationToken.None);

        var query = QueryOf(handler.Requests.Single());
        Assert.Equal(since, DateTime.Parse(query["updatedMin"]!, null, System.Globalization.DateTimeStyles.RoundtripKind));
        Assert.Equal("updated", query["orderBy"]);
        Assert.NotNull(query["timeMin"]);   // never the history, never an endless daily series
        Assert.NotNull(query["timeMax"]);
        Assert.Null(query["showDeleted"]);  // with updatedMin Google always sends the deleted ones anyway
    }

    // ---------- how events are understood ----------

    [Fact]
    public async Task TimedEvent_KeepsItsInstantsAndIsNotAllDay()
    {
        var e = await OneEvent(TimedEvent("t"));

        Assert.False(e.IsAllDay);
        Assert.Equal(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), e.StartUtc);   // 10:00+02:00
        Assert.Equal(new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), e.EndUtc);
        Assert.Equal("confirmed", e.Status);
    }

    [Fact]
    public async Task AllDayEvent_IsFlagged_AndKeepsGooglesExclusiveEnd()
    {
        var e = await OneEvent("""
            {"id": "d", "summary": "Conference", "status": "confirmed",
             "start": {"date": "2026-10-20"}, "end": {"date": "2026-10-22"}}
            """);

        Assert.True(e.IsAllDay);
        Assert.Equal(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), e.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc), e.EndUtc);   // 20th and 21st: the end is the day after
    }

    [Fact]
    public async Task EventDetails_AreCarriedOver()
    {
        var e = await OneEvent(TimedEvent("x", """
            , "eventType": "focusTime", "colorId": "11", "location": "Room 4, Main St",
              "transparency": "transparent", "recurringEventId": "series-1"
            """));

        Assert.Equal("focusTime", e.EventType);
        Assert.Equal("11", e.ColorId);
        Assert.Equal("Room 4, Main St", e.Location);
        Assert.True(e.IsFree);
        Assert.Equal("series-1", e.RecurringEventId);
    }

    [Fact]
    public async Task EventWithoutOptionalFields_HasSafeDefaults()
    {
        var e = await OneEvent(TimedEvent("plain"));

        Assert.Null(e.EventType);
        Assert.Null(e.ColorId);
        Assert.Null(e.Location);
        Assert.False(e.IsFree);   // Google's default is busy ("opaque")
        Assert.Null(e.RecurringEventId);
        Assert.Null(e.SettingsText);
        Assert.Empty(e.ReminderMinutes!); // no reminders block at all = none (not "unknown")
    }

    [Theory]
    [InlineData("""{"type": "homeOffice", "homeOffice": {}}""", "Home")]
    [InlineData("""{"type": "officeLocation", "officeLocation": {"label": "HQ, 3rd floor"}}""", "HQ, 3rd floor")]
    [InlineData("""{"type": "officeLocation", "officeLocation": {}}""", "Office")]
    [InlineData("""{"type": "customLocation", "customLocation": {"label": "Grandma's"}}""", "Grandma's")]
    [InlineData("""{"type": "customLocation", "customLocation": {}}""", "Other")]
    public async Task WorkingLocation_BecomesAReadableLabel(string properties, string expectedLocation)
    {
        var e = await OneEvent(TimedEvent("w", $", \"eventType\": \"workingLocation\", \"workingLocationProperties\": {properties}"));

        Assert.Equal(expectedLocation, e.Location);
    }

    [Fact]
    public async Task FocusTime_SettingsBecomeOneReadableLine()
    {
        var e = await OneEvent(TimedEvent("f", """
            , "eventType": "focusTime",
              "focusTimeProperties": {"autoDeclineMode": "declineOnlyNewConflictingInvitations",
                                      "declineMessage": "Writing my thesis", "chatStatus": "doNotDisturb"}
            """));

        Assert.Equal("Auto-decline: declines new conflicting invitations · Do not disturb · Message: \"Writing my thesis\"", e.SettingsText);
    }

    [Fact]
    public async Task OutOfOffice_WithNothingToSay_HasNoSettingsLine()
    {
        var e = await OneEvent(TimedEvent("o", """
            , "eventType": "outOfOffice", "outOfOfficeProperties": {"autoDeclineMode": "declineNone"}
            """));

        Assert.Null(e.SettingsText);
    }

    // ---------- reminders ----------

    [Fact]
    public async Task OwnReminders_AreReadAsMinutes_PopupAndEmailBoth()
    {
        var e = await OneEvent(TimedEvent("r", """
            , "reminders": {"useDefault": false, "overrides": [
                {"method": "popup", "minutes": 10}, {"method": "email", "minutes": 1440}, {"method": "sms", "minutes": 5}]}
            """));

        Assert.Equal([10, 1440], e.ReminderMinutes);   // the long-retired SMS method is ignored
    }

    [Fact]
    public async Task UseDefault_TakesTheCalendarsDefaultReminders()
    {
        var e = await OneEvent(
            TimedEvent("r", """, "reminders": {"useDefault": true}"""),
            defaultReminders: """[{"method": "popup", "minutes": 30}]""");

        Assert.Equal([30], e.ReminderMinutes);
    }

    [Fact]
    public async Task OverridesWin_OverTheCalendarDefaults_WhenUseDefaultIsFalse()
    {
        var e = await OneEvent(
            TimedEvent("r", """, "reminders": {"useDefault": false, "overrides": [{"method": "popup", "minutes": 5}]}"""),
            defaultReminders: """[{"method": "popup", "minutes": 30}]""");

        Assert.Equal([5], e.ReminderMinutes);
    }

    [Fact]
    public async Task EventWithNoReminders_HasAnEmptyList()
    {
        var e = await OneEvent(
            TimedEvent("r", """, "reminders": {"useDefault": false}"""),
            defaultReminders: """[{"method": "popup", "minutes": 30}]""");

        Assert.Empty(e.ReminderMinutes!);
    }

    [Fact]
    public async Task CancelledEvent_IsMarked_EvenWithoutTimes()
    {
        var e = await OneEvent("""{"id": "gone", "status": "cancelled"}""");

        Assert.True(e.IsCancelled);
        Assert.Null(e.StartUtc);
    }
}
