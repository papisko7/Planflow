namespace PlanFlow.Application.Common.Interfaces;

/// <summary>Delivers one e-mail. Only the outbox job calls this; handlers queue mail through INotificationService instead.</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken);
}
