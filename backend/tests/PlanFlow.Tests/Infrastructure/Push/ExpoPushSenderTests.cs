using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Infrastructure.Push;
using PlanFlow.Tests.Integration;

namespace PlanFlow.Tests.Infrastructure.Push;

/// <summary>Talks to a scripted stand-in for Expo's push service: what we send, and how every kind of answer is understood.</summary>
public class ExpoPushSenderTests
{
    private static ExpoPushSender Sender(StubHttpMessageHandler handler, string? accessToken = null) =>
        new(new HttpClient(handler), Options.Create(new PushOptions { Url = "https://push.test/send", AccessToken = accessToken }));

    private static PushMessage Message(string to = "ExponentPushToken[abc]") =>
        new(to, "New task", "Ann assigned it to you.", 3, new Dictionary<string, string> { ["taskId"] = "t-1" });

    [Fact]
    public async Task Send_PostsTheMessagesAsAJsonArray_WithSoundBadgeAndData()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"status":"ok","id":"1"}]}""");

        var results = await Sender(handler).SendAsync(new[] { Message() }, CancellationToken.None);

        Assert.True(Assert.Single(results).Accepted);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://push.test/send", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Send_TheBodyCarriesEveryField()
    {
        string? sentBody = null;
        var handler = new StubHttpMessageHandler().Enqueue(request =>
        {
            sentBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"status":"ok"}]}""", System.Text.Encoding.UTF8, "application/json")
            };
        });

        await Sender(handler).SendAsync(new[] { Message() }, CancellationToken.None);

        var item = JsonDocument.Parse(sentBody!).RootElement[0];
        Assert.Equal("ExponentPushToken[abc]", item.GetProperty("to").GetString());
        Assert.Equal("New task", item.GetProperty("title").GetString());
        Assert.Equal("Ann assigned it to you.", item.GetProperty("body").GetString());
        Assert.Equal(3, item.GetProperty("badge").GetInt32());
        Assert.Equal("default", item.GetProperty("sound").GetString());
        Assert.Equal("t-1", item.GetProperty("data").GetProperty("taskId").GetString());
    }

    [Fact]
    public async Task Send_AddsTheAccessTokenWhenOneIsConfigured()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"status":"ok"}]}""");

        await Sender(handler, accessToken: "secret").SendAsync(new[] { Message() }, CancellationToken.None);

        Assert.Equal("Bearer secret", handler.Requests.Single().Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Send_UnderstandsOkErrorAndDeadDeviceAnswers_InOrder()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
            {"data":[
              {"status":"ok","id":"1"},
              {"status":"error","message":"The recipient device is not registered","details":{"error":"DeviceNotRegistered"}},
              {"status":"error","message":"Too many messages","details":{"error":"MessageRateExceeded"}}
            ]}
            """);

        var results = await Sender(handler).SendAsync(new[] { Message("a"), Message("b"), Message("c") }, CancellationToken.None);

        Assert.True(results[0].Accepted);
        Assert.False(results[1].Accepted);
        Assert.True(results[1].TokenInvalid);
        Assert.False(results[2].Accepted);
        Assert.False(results[2].TokenInvalid);
        Assert.Equal("Too many messages", results[2].Error);
    }

    [Fact]
    public async Task Send_MoreThan100Messages_AreSplitIntoSeveralCalls()
    {
        var one = """{"status":"ok"}""";
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, $$"""{"data":[{{string.Join(",", Enumerable.Repeat(one, 100))}}]}""")
            .Enqueue(HttpStatusCode.OK, $$"""{"data":[{{string.Join(",", Enumerable.Repeat(one, 50))}}]}""");

        var results = await Sender(handler).SendAsync(Enumerable.Range(0, 150).Select(i => Message($"t{i}")).ToList(), CancellationToken.None);

        Assert.Equal(150, results.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Send_WhenTheWholeCallFails_Throws_SoTheJobRetriesLater(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler().Enqueue(status);

        await Assert.ThrowsAsync<HttpRequestException>(() => Sender(handler).SendAsync(new[] { Message() }, CancellationToken.None));
    }

    [Fact]
    public async Task Send_AnAnswerWithTheWrongNumberOfResults_IsRejected()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{"data":[]}""");

        await Assert.ThrowsAsync<HttpRequestException>(() => Sender(handler).SendAsync(new[] { Message() }, CancellationToken.None));
    }
}
