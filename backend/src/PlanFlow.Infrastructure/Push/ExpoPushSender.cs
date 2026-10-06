using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Infrastructure.Push;

/// <summary>
/// Push settings (configuration section "Push"). The Expo push service is free and needs no account to send, so the defaults
/// work as they are. <see cref="AccessToken"/> is only needed if "enhanced push security" is switched on for the Expo project
/// — set it through <c>dotnet user-secrets</c> / environment variables (Push__AccessToken), never in a committed file.
/// </summary>
public class PushOptions
{
    public string Url { get; set; } = "https://exp.host/--/api/v2/push/send";
    public string? AccessToken { get; set; }
}

/// <summary>
/// Sends pushes through Expo's push service, which forwards them to Apple (APNs) / Google (FCM). One HTTP call carries up to
/// 100 messages; the answer lists one verdict per message in the same order.
/// </summary>
public class ExpoPushSender : IPushSender
{
    private const int MaxBatch = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly PushOptions _options;

    public ExpoPushSender(HttpClient http, IOptions<PushOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        var results = new List<PushResult>(messages.Count);
        foreach (var batch in messages.Chunk(MaxBatch))
        {
            results.AddRange(await SendBatchAsync(batch, cancellationToken));
        }

        return results;
    }

    private async Task<IReadOnlyList<PushResult>> SendBatchAsync(PushMessage[] batch, CancellationToken cancellationToken)
    {
        var payload = batch.Select(m => new
        {
            to = m.To,
            title = m.Title,
            body = m.Body,
            badge = m.Badge,
            data = m.Data,
            sound = "default",
            channelId = "default",
            priority = "high"
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url) { Content = JsonContent.Create(payload, options: Json) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(_options.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The whole call failed (service down, rate limited, ...): the job retries the batch later.
            throw new HttpRequestException($"Expo push service answered {(int)response.StatusCode}.");
        }

        var body = await response.Content.ReadFromJsonAsync<ExpoResponse>(Json, cancellationToken);
        if (body?.Data is null || body.Data.Count != batch.Length)
        {
            throw new HttpRequestException("Expo push service returned an unexpected answer.");
        }

        return body.Data.Select(t =>
        {
            if (t.Status == "ok")
            {
                return new PushResult(true, false, null);
            }

            // "DeviceNotRegistered" = the app was uninstalled or the token revoked: it will never work again.
            var invalid = t.Details?.Error == "DeviceNotRegistered";
            return new PushResult(false, invalid, t.Message ?? t.Details?.Error ?? "unknown error");
        }).ToList();
    }

    private record ExpoResponse(List<ExpoTicket>? Data);

    private record ExpoTicket(string? Status, string? Message, ExpoDetails? Details);

    private record ExpoDetails(string? Error);
}
