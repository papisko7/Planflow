using System.Net;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.ExternalServices.Google;

namespace PlanFlow.Tests.Integration;

/// <summary>
/// Step 7.5.3: the real <see cref="GoogleTasksClient"/> through the production retry pipeline, with Google replaced by a stub transport
/// (same approach as <see cref="CalendarSyncIntegrationTests"/>): real JSON, real paging, real query strings.
/// </summary>
public class GoogleTasksClientTests
{
    private static (IGoogleTasksClient Client, ServiceProvider Provider) Build(StubHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient<IGoogleTasksClient, GoogleTasksClient>()
            .AddGoogleCalendarRetry()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IGoogleTasksClient>(), provider);
    }

    [Theory]
    [InlineData("2026-10-20T00:00:00.000Z", 2026, 10, 20)]
    [InlineData("2026-01-05T00:00:00Z", 2026, 1, 5)]
    [InlineData("2026-12-31", 2026, 12, 31)] // a bare date is read the same way
    public void DueDay_IsTheDatePartRead_LiterallyWithoutZoneConversion(string due, int year, int month, int day)
    {
        var parsed = GoogleTasksClient.DueDay(due);

        Assert.Equal(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("2026-13-45T00:00:00Z")]
    public void DueDay_IsNullWhenThereIsNoUsableDate(string? due) => Assert.Null(GoogleTasksClient.DueDay(due));

    [Fact]
    public async Task ListTaskLists_ReadsEveryPage()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{ "items": [ { "id": "a", "title": "My Tasks" } ], "nextPageToken": "page-2" }""")
            .Enqueue(HttpStatusCode.OK, """{ "items": [ { "id": "b", "title": "Shopping" }, { "title": "no id, ignored" } ] }""");
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var lists = await client.ListTaskListsAsync("token", CancellationToken.None);

        Assert.Equal(["My Tasks", "Shopping"], lists.Select(l => l.Title));
        Assert.Equal("Bearer token", handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Contains("pageToken=page-2", handler.Requests[1].RequestUri!.Query);
        Assert.StartsWith("https://tasks.googleapis.com/tasks/v1/users/@me/lists", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task ListTasks_MapsFieldsAndReadsEveryPage()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """
                { "items": [
                    { "id": "t1", "title": "Write", "notes": "Chapter 3", "status": "needsAction", "due": "2026-10-20T00:00:00.000Z" },
                    { "id": "t2", "title": "Done", "status": "completed", "completed": "2026-10-01T10:00:00.000Z" }
                  ], "nextPageToken": "next" }
                """)
            .Enqueue(HttpStatusCode.OK, """
                { "items": [
                    { "id": "t3", "status": "needsAction", "deleted": true },
                    { "id": "t4", "title": "Sub", "status": "needsAction", "parent": "t1" }
                  ] }
                """);
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var tasks = await client.ListTasksAsync("token", "list-1", null, CancellationToken.None);

        Assert.Equal(4, tasks.Count);
        var write = tasks[0];
        Assert.Equal(("t1", "Write", "Chapter 3"), (write.Id, write.Title, write.Notes));
        Assert.Equal(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), write.DueDay);
        Assert.False(write.IsCompleted);
        Assert.True(tasks[1].IsCompleted);
        Assert.Null(tasks[1].DueDay);
        Assert.True(tasks[2].IsDeleted);
        Assert.True(tasks[3].IsSubtask);
        Assert.False(write.IsSubtask);
    }

    [Fact]
    public async Task ListTasks_AsksForCompletedHiddenAndDeletedTasks_SoChangesMadeInGoogleAreSeen()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{ "items": [] }""");
        var (client, provider) = Build(handler);
        await using var _ = provider;
        var since = new DateTime(2026, 10, 18, 12, 30, 0, DateTimeKind.Utc);

        await client.ListTasksAsync("token", "list/with space", since, CancellationToken.None);

        var uri = handler.Requests.Single().RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("true", query["showCompleted"]);
        Assert.Equal("true", query["showHidden"]);
        Assert.Equal("true", query["showDeleted"]);
        Assert.Equal("100", query["maxResults"]);
        Assert.StartsWith("2026-10-18T12:30:00", query["updatedMin"]);
        Assert.Contains("/lists/list%2Fwith%20space/tasks", uri.AbsoluteUri); // the list id is escaped, not trusted
    }

    [Fact]
    public async Task ListTasks_WithoutAWatermark_DoesNotSendUpdatedMin()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{ "items": [] }""");
        var (client, provider) = Build(handler);
        await using var _ = provider;

        await client.ListTasksAsync("token", "l1", null, CancellationToken.None);

        Assert.DoesNotContain("updatedMin", handler.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task Unauthorized_IsReportedAsUnauthorizedSoTheJobCanRefreshTheToken()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized);
        var (client, provider) = Build(handler);
        await using var _ = provider;

        await Assert.ThrowsAsync<UnauthorizedException>(() => client.ListTaskListsAsync("expired", CancellationToken.None));
    }

    [Fact]
    public async Task TransientGoogleFailure_IsRetriedBeforeItReachesTheJob()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable)
            .Enqueue(HttpStatusCode.OK, """{ "items": [ { "id": "a", "title": "My Tasks" } ] }""");
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var lists = await client.ListTaskListsAsync("token", CancellationToken.None);

        Assert.Single(lists);
        Assert.Equal(2, handler.Requests.Count);
    }
}
