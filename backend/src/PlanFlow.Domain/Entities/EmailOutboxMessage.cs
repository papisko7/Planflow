using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// An e-mail waiting to be sent. Handlers add a row in the SAME transaction as the change that caused it
/// (outbox pattern), so a mail is never lost or sent for a change that was rolled back, and a slow or down
/// mail server never makes a request fail. <c>EmailOutboxJob</c> delivers the rows and retries failures.
/// </summary>
public class EmailOutboxMessage : BaseEntity
{
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string TextBody { get; set; } = string.Empty;
    public NotificationEvent Event { get; set; }

    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public string? LastError { get; set; }
}
