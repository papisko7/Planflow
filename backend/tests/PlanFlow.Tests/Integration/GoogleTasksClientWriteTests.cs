using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.ExternalServices.Google;

namespace PlanFlow.Tests.Integration;

/// <summary>The real <see cref="GoogleTasksClient"/> reading and changing one task, with Google replaced by a stub transport.</summary>
public class GoogleTasksClientWriteTests
{
    private const string OpenTask = """
        { "id": "t1", "title": "Write", "notes": "Chapter 3", "status": "needsAction", "due": "2026-11-10T00:00:00.000Z", "updated": "2026-10-20T08:30:15.000Z" }
        """;

    private static (IGoogleTasksClient Client, ServiceProvider Provider) Build(StubHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient<IGoogleTasksClient, GoogleTasksClient>()
            .AddGoogleCalendarRetry()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IGoogleTasksClient>(), provider);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetTask_MapsTheTaskLikeTheListDoes_AndKeepsGoogleUpdateTime()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, OpenTask);
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var task = await client.GetTaskAsync("token", "list/1", "t1", CancellationToken.None);

        Assert.NotNull(task);
        Assert.Equal(("t1", "Write", "Chapter 3"), (task.Id, task.Title, task.Notes));
        Assert.Equal(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), task.DueDay);
        Assert.False(task.IsCompleted);
        Assert.Equal(new DateTime(2026, 10, 20, 8, 30, 15, DateTimeKind.Utc), task.UpdatedUtc);
        Assert.Equal("https://tasks.googleapis.com/tasks/v1/lists/list%2F1/tasks/t1", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer token", handler.Requests[0].Headers.Authorization!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task GetTask_ThatNoLongerExists_IsNull(HttpStatusCode status)
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(status));
        await using var _ = provider;

        Assert.Null(await client.GetTaskAsync("token", "l1", "t1", CancellationToken.None));
    }

    [Fact]
    public async Task GetTask_DeletedButStillReported_ComesBackMarkedDeleted()
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{ "id": "t1", "deleted": true }"""));
        await using var _ = provider;

        Assert.True((await client.GetTaskAsync("token", "l1", "t1", CancellationToken.None))!.IsDeleted);
    }

    [Fact]
    public async Task PatchTask_SendsTitleNotesDueDayAndDone_AsGoogleWritesThem()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler().Enqueue(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, OpenTask);
        });
        var (client, provider) = Build(handler);
        await using var _ = provider;

        var patch = new GoogleTaskPatch("Write 4", "Sources", new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc), IsCompleted: true);
        var after = await client.PatchTaskAsync("token", "l1", "t1", patch, CancellationToken.None);

        Assert.Equal(new HttpMethod("PATCH"), handler.Requests[0].Method);
        Assert.Equal("https://tasks.googleapis.com/tasks/v1/lists/l1/tasks/t1", handler.Requests[0].RequestUri!.AbsoluteUri);
        var json = JsonNode.Parse(body!)!.AsObject();
        Assert.Equal("Write 4", (string?)json["title"]);
        Assert.Equal("Sources", (string?)json["notes"]);
        Assert.Equal("2026-11-12T00:00:00.000Z", (string?)json["due"]);   // a whole day, the time of day means nothing to Google Tasks
        Assert.Equal("completed", (string?)json["status"]);
        Assert.False(json.ContainsKey("completed"));
        Assert.Equal("Write", after.Title);                                  // the answer is the task as Google now holds it
    }

    [Fact]
    public async Task PatchTask_Reopening_AlsoClearsTheCompletionTime()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler().Enqueue(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, OpenTask);
        });
        var (client, provider) = Build(handler);
        await using var _ = provider;

        await client.PatchTaskAsync("token", "l1", "t1", new GoogleTaskPatch(IsCompleted: false), CancellationToken.None);

        var json = JsonNode.Parse(body!)!.AsObject();
        Assert.Equal("needsAction", (string?)json["status"]);
        Assert.True(json.ContainsKey("completed"));
        Assert.Null(json["completed"]);
        Assert.False(json.ContainsKey("title"));                             // only what was named is sent
    }

    [Fact]
    public async Task PatchTask_AccountDidNotAllowChanges_IsAMissingPermission()
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Forbidden, """
            { "error": { "code": 403, "message": "Request had insufficient authentication scopes.", "status": "PERMISSION_DENIED",
              "details": [ { "reason": "ACCESS_TOKEN_SCOPE_INSUFFICIENT" } ] } }
            """));
        await using var _ = provider;

        var ex = await Assert.ThrowsAsync<GoogleWriteRejectedException>(() =>
            client.PatchTaskAsync("token", "l1", "t1", new GoogleTaskPatch(Title: "x"), CancellationToken.None));

        Assert.Equal(GoogleWriteFailure.MissingPermission, ex.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, GoogleWriteFailure.Gone)]
    [InlineData(HttpStatusCode.BadRequest, GoogleWriteFailure.Rejected)]
    public async Task PatchTask_FinalAnswers_AreSortedIntoGoneAndRefused(HttpStatusCode status, GoogleWriteFailure expected)
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(status, """{ "error": { "message": "no" } }"""));
        await using var _ = provider;

        var ex = await Assert.ThrowsAsync<GoogleWriteRejectedException>(() =>
            client.PatchTaskAsync("token", "l1", "t1", new GoogleTaskPatch(Title: "x"), CancellationToken.None));

        Assert.Equal(expected, ex.Failure);
    }

    [Fact]
    public async Task PatchTask_RejectedToken_IsReportedSoTheCallerCanRefreshIt()
    {
        var (client, provider) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized));
        await using var _ = provider;

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            client.PatchTaskAsync("token", "l1", "t1", new GoogleTaskPatch(Title: "x"), CancellationToken.None));
    }
}
