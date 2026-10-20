using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.ExternalServices.Google;

namespace PlanFlow.Tests.Integration;

/// <summary>
/// The real <see cref="GoogleCalendarClient"/> reading and changing one event, through the production retry pipeline, with Google replaced
/// by a stub transport: real JSON, real URLs, real request bodies.
/// </summary>
public class GoogleCalendarClientWriteTests
{
    private const string TimedEvent = """
        { "id": "e1", "status": "confirmed", "summary": "Standup", "location": "Room 1", "colorId": "5", "updated": "2026-10-20T08:30:15.123Z",
          "start": { "dateTime": "2026-11-03T10:00:00+01:00" }, "end": { "dateTime": "2026-11-03T11:00:00+01:00" },
          "reminders": { "useDefault": false, "overrides": [ { "method": "popup", "minutes": 10 }, { "method": "email", "minutes": 60 } ] } }
        """;

    private static (IGoogleCalendarClient Client, ServiceProvider Provider) Build(StubHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>()
            .AddGoogleCalendarRetry()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IGoogleCalendarClient>(), provider);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---- reading one event ----

    [Fact]
    public async Task GetEvent_MapsTheEventLikeTheListDoes_AndKeepsGoogleUpdateTime()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, TimedEvent);
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var e = await client.GetEventAsync("token", "e1", CancellationToken.None);

        Assert.NotNull(e);
        Assert.Equal(("e1", "Standup", "Room 1", "5"), (e.Id, e.Title, e.Location, e.ColorId));
        Assert.Equal(new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc), e.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc), e.EndUtc);
        Assert.Equal([10, 60], e.ReminderMinutes);
        Assert.Equal(new DateTime(2026, 10, 20, 8, 30, 15, 123, DateTimeKind.Utc), e.UpdatedUtc);
        Assert.Equal("Bearer token", handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("https://www.googleapis.com/calendar/v3/calendars/primary/events/e1", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetEvent_OfARepeatingEventInstance_EscapesTheId()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, TimedEvent);
        var (client, provider) = Build(handler);
        await using var _ = provider;

        await client.GetEventAsync("token", "abc_20261103T090000Z", CancellationToken.None);

        Assert.EndsWith("/events/abc_20261103T090000Z", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetEvent_ThatUsesTheCalendarDefaults_AsksForThemWithOneMoreSmallRequest()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """
                { "id": "e1", "summary": "Standup", "start": { "dateTime": "2026-11-03T10:00:00Z" }, "end": { "dateTime": "2026-11-03T11:00:00Z" },
                  "reminders": { "useDefault": true } }
                """)
            .Enqueue(HttpStatusCode.OK, """{ "items": [], "defaultReminders": [ { "method": "popup", "minutes": 30 } ] }""");
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var e = await client.GetEventAsync("token", "e1", CancellationToken.None);

        Assert.Equal([30], e!.ReminderMinutes);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("maxResults=1", handler.Requests[1].RequestUri!.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task GetEvent_ThatNoLongerExists_IsNull(HttpStatusCode status)
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(status));
        await using var _ = provider;

        Assert.Null(await client.GetEventAsync("token", "e1", CancellationToken.None));
    }

    [Fact]
    public async Task GetEvent_RejectedToken_IsReportedSoTheCallerCanRefreshIt()
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized));
        await using var _ = provider;

        await Assert.ThrowsAsync<UnauthorizedException>(() => client.GetEventAsync("token", "e1", CancellationToken.None));
    }

    // ---- changing one event ----

    [Fact]
    public async Task PatchEvent_SendsOnlyWhatIsNamed_WithPatchAndWithoutMailingTheGuests()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler().Enqueue(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, TimedEvent);
        });
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var after = await client.PatchEventAsync("token", "e1", new GoogleEventPatch(Title: "Daily", Location: ""), CancellationToken.None);

        Assert.Equal(new HttpMethod("PATCH"), handler.Requests[0].Method);
        Assert.Equal("https://www.googleapis.com/calendar/v3/calendars/primary/events/e1?sendUpdates=none", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("Bearer token", handler.Requests[0].Headers.Authorization!.ToString());
        var json = JsonNode.Parse(body!)!.AsObject();
        Assert.Equal(["location", "summary"], json.Select(p => p.Key).Order());
        Assert.Equal("Daily", (string?)json["summary"]);
        Assert.Equal(string.Empty, (string?)json["location"]);
        Assert.Equal("Standup", after.Title);                       // the answer is the event as Google now holds it
        Assert.NotNull(after.UpdatedUtc);
    }

    [Fact]
    public async Task PatchEvent_TimedEvent_SendsUtcDateTimes_AndClearsTheDateOfAFormerAllDayEvent()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler().Enqueue(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, TimedEvent);
        });
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var time = new GoogleEventTime(new DateTime(2026, 11, 3, 14, 30, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc), IsAllDay: false);
        await client.PatchEventAsync("token", "e1", new GoogleEventPatch(Time: time), CancellationToken.None);

        var json = JsonNode.Parse(body!)!.AsObject();
        Assert.Equal("2026-11-03T14:30:00.000Z", (string?)json["start"]!["dateTime"]);
        Assert.Equal("2026-11-03T15:00:00.000Z", (string?)json["end"]!["dateTime"]);
        Assert.True(json["start"]!.AsObject().ContainsKey("date"));
        Assert.Null(json["start"]!["date"]);                        // an explicit null removes the old all-day date
    }

    [Fact]
    public async Task PatchEvent_AllDayEvent_SendsDates_AndClearsTheDateTime()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler().Enqueue(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, TimedEvent);
        });
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var time = new GoogleEventTime(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), IsAllDay: true);
        await client.PatchEventAsync("token", "e1", new GoogleEventPatch(Time: time), CancellationToken.None);

        var json = JsonNode.Parse(body!)!.AsObject();
        Assert.Equal("2026-11-03", (string?)json["start"]!["date"]);
        Assert.Equal("2026-11-05", (string?)json["end"]!["date"]);   // the day AFTER the last one, as Google counts it
        Assert.Null(json["start"]!["dateTime"]);
        Assert.True(json["start"]!.AsObject().ContainsKey("dateTime"));
    }

    [Fact]
    public async Task PatchEvent_ColourBusyAndReminders_AreWrittenTheWayGoogleReadsThem()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler().Enqueue(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, TimedEvent);
        });
        var (client, provider) = Build(handler);
        await using var _ = provider;

        await client.PatchEventAsync("token", "e1", new GoogleEventPatch(ColorId: "11", IsFree: true, ReminderMinutes: [10, 60]), CancellationToken.None);

        var json = JsonNode.Parse(body!)!.AsObject();
        Assert.Equal("11", (string?)json["colorId"]);
        Assert.Equal("transparent", (string?)json["transparency"]);
        Assert.False((bool)json["reminders"]!["useDefault"]!);       // the person's own list replaces the calendar's defaults
        Assert.Equal([10, 60], json["reminders"]!["overrides"]!.AsArray().Select(o => (int)o!["minutes"]!));
        Assert.All(json["reminders"]!["overrides"]!.AsArray(), o => Assert.Equal("popup", (string?)o!["method"]));
    }

    [Fact]
    public async Task PatchEvent_EmptyColourAndNoReminders_RemoveThemInGoogle()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler().Enqueue(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, TimedEvent);
        });
        var (client, provider) = Build(handler);
        await using var _ = provider;

        await client.PatchEventAsync("token", "e1", new GoogleEventPatch(ColorId: "", IsFree: false, ReminderMinutes: []), CancellationToken.None);

        var json = JsonNode.Parse(body!)!.AsObject();
        Assert.True(json.ContainsKey("colorId"));
        Assert.Null(json["colorId"]);                                // JSON null = the calendar's own colour again
        Assert.Equal("opaque", (string?)json["transparency"]);
        Assert.Empty(json["reminders"]!["overrides"]!.AsArray());
    }

    // ---- when Google says no ----

    [Fact]
    public async Task PatchEvent_RejectedToken_IsReportedSoTheCallerCanRefreshIt()
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized));
        await using var _ = provider;

        await Assert.ThrowsAsync<UnauthorizedException>(() => client.PatchEventAsync("token", "e1", new GoogleEventPatch(Title: "x"), CancellationToken.None));
    }

    [Fact]
    public async Task PatchEvent_AccountDidNotAllowChanges_IsAMissingPermission()
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Forbidden, """
            { "error": { "code": 403, "message": "Request had insufficient authentication scopes.", "errors": [ { "reason": "insufficientPermissions" } ] } }
            """));
        await using var _ = provider;

        var ex = await Assert.ThrowsAsync<GoogleWriteRejectedException>(() =>
            client.PatchEventAsync("token", "e1", new GoogleEventPatch(Title: "x"), CancellationToken.None));

        Assert.Equal(GoogleWriteFailure.MissingPermission, ex.Failure);
    }

    [Fact]
    public async Task PatchEvent_EventOwnedBySomebodyElse_IsARefusal()
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Forbidden, """
            { "error": { "code": 403, "message": "You need to have writer access to this calendar.", "errors": [ { "reason": "requiredAccessLevel" } ] } }
            """));
        await using var _ = provider;

        var ex = await Assert.ThrowsAsync<GoogleWriteRejectedException>(() =>
            client.PatchEventAsync("token", "e1", new GoogleEventPatch(Title: "x"), CancellationToken.None));

        Assert.Equal(GoogleWriteFailure.Rejected, ex.Failure);
    }

    [Fact]
    public async Task PatchEvent_RateLimitAnsweredWith403_IsATemporaryFailureNotARefusal()
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Forbidden, """
            { "error": { "code": 403, "message": "Rate Limit Exceeded", "errors": [ { "reason": "rateLimitExceeded" } ] } }
            """));
        await using var _ = provider;

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PatchEventAsync("token", "e1", new GoogleEventPatch(Title: "x"), CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, GoogleWriteFailure.Gone)]
    [InlineData(HttpStatusCode.Gone, GoogleWriteFailure.Gone)]
    [InlineData(HttpStatusCode.BadRequest, GoogleWriteFailure.Rejected)]
    [InlineData(HttpStatusCode.Conflict, GoogleWriteFailure.Rejected)]
    [InlineData(HttpStatusCode.PreconditionFailed, GoogleWriteFailure.Rejected)]
    public async Task PatchEvent_FinalAnswers_AreSortedIntoGoneAndRefused(HttpStatusCode status, GoogleWriteFailure expected)
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(status, """{ "error": { "message": "no" } }"""));
        await using var _ = provider;

        var ex = await Assert.ThrowsAsync<GoogleWriteRejectedException>(() =>
            client.PatchEventAsync("token", "e1", new GoogleEventPatch(Title: "x"), CancellationToken.None));

        Assert.Equal(expected, ex.Failure);
    }

    [Fact]
    public async Task PatchEvent_TemporaryGoogleFailure_IsRetriedThenReportedAsAFailureToTryAgain()
    {
        var handler = new StubHttpMessageHandler();
        for (var i = 0; i < 4; i++)   // the first try plus three retries
        {
            handler.Enqueue(HttpStatusCode.ServiceUnavailable);
        }
        var (client, provider) = Build(handler);
        await using var _ = provider;

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PatchEventAsync("token", "e1", new GoogleEventPatch(Title: "x"), CancellationToken.None));

        Assert.Equal(4, handler.Requests.Count);
    }
}
