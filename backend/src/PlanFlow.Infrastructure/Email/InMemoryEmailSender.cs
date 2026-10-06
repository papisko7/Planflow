using System.Collections.Concurrent;
using PlanFlow.Application.Common.Interfaces;

namespace PlanFlow.Infrastructure.Email;

/// <summary>Keeps sent mail in memory instead of sending it. Registered only under the test environments so tests can read the "inbox".</summary>
public class InMemoryEmailSender : IEmailSender
{
    public record SentEmail(string To, string Subject, string HtmlBody, string TextBody);

    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyCollection<SentEmail> Sent => _sent.ToArray();

    public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken)
    {
        _sent.Enqueue(new SentEmail(toEmail, subject, htmlBody, textBody));
        return Task.CompletedTask;
    }
}
