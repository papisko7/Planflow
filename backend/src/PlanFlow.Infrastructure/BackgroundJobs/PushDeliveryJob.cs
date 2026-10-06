using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using Quartz;

namespace PlanFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Hands pending inbox entries to the push service so they pop up on the phone. Same idea as the e-mail outbox: handlers only
/// write rows, this job does the slow network part, and a failed attempt is retried with a growing delay (10 s, 20 s, 40 s, ...).
/// A phone the push service reports as gone ("DeviceNotRegistered") is forgotten.
/// </summary>
[DisallowConcurrentExecution]
public class PushDeliveryJob : IJob
{
    private const int BatchSize = 50;
    public const int MaxAttempts = 5;

    private readonly IApplicationDbContext _context;
    private readonly IPushSender _sender;
    private readonly ILogger<PushDeliveryJob> _logger;

    public PushDeliveryJob(IApplicationDbContext context, IPushSender sender, ILogger<PushDeliveryJob> logger)
    {
        _context = context;
        _sender = sender;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var nowUtc = DateTime.UtcNow;

        var due = await _context.Notifications
            .Where(n => n.PushState == PushDeliveryState.Pending && n.PushAttempts < MaxAttempts && n.PushNextAttemptAtUtc <= nowUtc)
            .OrderBy(n => n.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return;
        }

        var userIds = due.Select(n => n.UserId).Distinct().ToList();
        var tokensByUser = (await _context.PushTokens
                .Where(t => userIds.Contains(t.UserId))
                .ToListAsync(cancellationToken))
            .ToLookup(t => t.UserId);

        // The number shown on the app icon: how many entries the user has not opened.
        var unreadByUser = await _context.Notifications
            .Where(n => userIds.Contains(n.UserId) && n.ReadAtUtc == null)
            .GroupBy(n => n.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, cancellationToken);

        // One message per (notification, phone). A user without any registered phone has nobody to push to — that is done, not failed.
        var messages = new List<PushMessage>();
        var owners = new List<(Notification Notification, PushToken Token)>();
        foreach (var notification in due)
        {
            var tokens = tokensByUser[notification.UserId].ToList();
            if (tokens.Count == 0)
            {
                notification.PushState = PushDeliveryState.Sent;
                continue;
            }

            foreach (var token in tokens)
            {
                messages.Add(new PushMessage(token.Token, notification.Title, notification.Body, unreadByUser.GetValueOrDefault(notification.UserId), DataFor(notification)));
                owners.Add((notification, token));
            }
        }

        if (messages.Count > 0)
        {
            await DeliverAsync(messages, owners, cancellationToken);
        }

        foreach (var notification in due)
        {
            notification.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("PushDeliveryJob handled {Due} due notifications ({Messages} pushes).", due.Count, messages.Count);
    }

    private async Task DeliverAsync(
        List<PushMessage> messages, List<(Notification Notification, PushToken Token)> owners, CancellationToken cancellationToken)
    {
        IReadOnlyList<PushResult> results;
        try
        {
            results = await _sender.SendAsync(messages, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Push service unreachable; will retry {Count} notifications.", owners.Select(o => o.Notification).Distinct().Count());
            foreach (var notification in owners.Select(o => o.Notification).Distinct())
            {
                RecordFailure(notification, ex.Message);
            }

            return;
        }

        var accepted = new HashSet<Guid>();
        var failures = new Dictionary<Guid, (Notification Notification, string Error)>();
        for (var i = 0; i < owners.Count; i++)
        {
            var (notification, token) = owners[i];
            var result = results[i];
            if (result.Accepted)
            {
                accepted.Add(notification.Id);
            }
            else if (result.TokenInvalid)
            {
                _context.PushTokens.Remove(token);
            }
            else
            {
                failures[notification.Id] = (notification, result.Error ?? "unknown error");
            }
        }

        foreach (var notification in owners.Select(o => o.Notification).Distinct())
        {
            if (accepted.Contains(notification.Id) || !failures.ContainsKey(notification.Id))
            {
                // Delivered to at least one phone, or every phone turned out to be gone: nothing left to retry.
                notification.PushState = PushDeliveryState.Sent;
                notification.PushLastError = null;
            }
            else
            {
                RecordFailure(notification, failures[notification.Id].Error);
            }
        }
    }

    private static void RecordFailure(Notification notification, string error)
    {
        notification.PushAttempts++;
        notification.PushNextAttemptAtUtc = DateTime.UtcNow.AddSeconds(10 * Math.Pow(2, notification.PushAttempts - 1));
        notification.PushLastError = error.Length > 500 ? error[..500] : error;
        if (notification.PushAttempts >= MaxAttempts)
        {
            notification.PushState = PushDeliveryState.Failed;
        }
    }

    // What the app receives next to the text: enough to open the right screen when the push is tapped.
    private static Dictionary<string, string> DataFor(Notification n)
    {
        var data = new Dictionary<string, string>
        {
            ["notificationId"] = n.Id.ToString(),
            ["event"] = ((int)n.Event).ToString()
        };
        if (n.TeamId is { } teamId)
        {
            data["teamId"] = teamId.ToString();
        }

        if (n.TaskId is { } taskId)
        {
            data["taskId"] = taskId.ToString();
        }

        return data;
    }
}
