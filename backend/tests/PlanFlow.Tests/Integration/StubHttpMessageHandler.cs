using System.Net;
using System.Text;

namespace PlanFlow.Tests.Integration;

/// <summary>
/// Minimal scriptable HttpMessageHandler standing in for Google's servers so integration tests can
/// exercise the real HttpClient pipeline (including the resilience/retry handler registered in
/// DependencyInjection) without a mock-server dependency or real network calls.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public StubHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    public StubHttpMessageHandler Enqueue(HttpStatusCode statusCode, string? jsonBody = null) =>
        Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = jsonBody is null ? null : new StringContent(jsonBody, Encoding.UTF8, "application/json")
        });

    public StubHttpMessageHandler EnqueueTimeout() =>
        Enqueue(_ => throw new TaskCanceledException("Simulated network timeout talking to Google."));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"StubHttpMessageHandler received an unscripted request: {request.Method} {request.RequestUri}");
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
