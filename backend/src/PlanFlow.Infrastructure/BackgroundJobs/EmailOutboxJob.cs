using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Interfaces;
using Quartz;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Delivers queued e-mails (the outbox). Handlers only write rows; this job does the slow network part, so a request
/// never waits for (or fails because of) the mail server. A failed send is retried with a growing delay, then given up.
/// </summary>
[DisallowConcurrentExecution]
public class EmailOutboxJob : IJob
{
    private const int BatchSize = 20;
    public const int MaxAttempts = 5;

    private readonly IApplicationDbContext _context;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailOutboxJob> _logger;

    public EmailOutboxJob(IApplicationDbContext context, IEmailSender sender, ILogger<EmailOutboxJob> logger)
    {
        _context = context;
        _sender = sender;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var nowUtc = DateTime.UtcNow;

        var due = await _context.EmailOutbox
            .Where(m => m.SentAtUtc == null && m.Attempts < MaxAttempts && m.NextAttemptAtUtc <= nowUtc)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return;
        }

        var sent = 0;
        foreach (var message in due)
        {
            try
            {
                await _sender.SendAsync(message.ToEmail, message.Subject, message.HtmlBody, message.TextBody, cancellationToken);
                message.SentAtUtc = DateTime.UtcNow;
                message.LastError = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Retry after 1, 2, 4, 8 minutes; after MaxAttempts the row stays for inspection and is purged by CleanupJob.
                message.Attempts++;
                message.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(Math.Pow(2, message.Attempts - 1));
                message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                _logger.LogWarning(ex, "Could not send e-mail {MessageId} (attempt {Attempt}/{Max}).", message.Id, message.Attempts, MaxAttempts);
            }

            message.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("EmailOutboxJob sent {Sent} of {Due} due e-mails.", sent, due.Count);
    }
}
