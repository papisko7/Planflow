using PlanFlow.Domain.Enums;

namespace PlanFlow.Application.Notifications;

/// <summary>
/// Queues notifications for the outbox (e-mail) and the inbox (in-app entry + push). Nothing is sent or saved here: the rows
/// are added to the caller's DbContext, so they are committed (or rolled back) together with the change that caused them —
/// the caller's own <c>SaveChangesAsync</c> is what makes them real.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Queues <paramref name="evt"/> for every recipient who wants it. Applies, in order: the actor is never notified of
    /// their own action; the per-team mute silences every channel; then each channel decides separately — e-mail (confirmed
    /// address + master switch + category), inbox (category) and push (inbox + push master switch).
    /// </summary>
    /// <param name="taskId">The task the event is about, so tapping the inbox entry or the push can open it.</param>
    Task NotifyAsync(
        NotificationEvent evt,
        IEnumerable<Guid> recipientUserIds,
        Func<string, EmailContent> build,
        Guid? teamId = null,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default,
        Guid? taskId = null);

    /// <summary>Queues a mail to an address without consulting any preference — for mandatory mails to a user that may not be saved yet (registration).</summary>
    void QueueDirect(NotificationEvent evt, string toEmail, string recipientName, Func<string, EmailContent> build);
}
