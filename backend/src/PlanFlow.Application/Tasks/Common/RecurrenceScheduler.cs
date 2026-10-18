using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Decides whether a finished recurring item gets a successor and builds it. Pure logic (no database),
/// so the cycle rules can be unit-tested; the Quartz <c>RecurringItemJob</c> only loads, calls this and saves.
/// </summary>
public static class RecurrenceScheduler
{
    /// <summary>
    /// Builds the next instance of <paramref name="finished"/>, or returns null when the cycle is over.
    /// The next date is the old one plus the interval; if that is already in the past (the item was finished
    /// late) whole intervals are added until it is in the future, so the user is never handed an overdue copy.
    /// The cycle ends when "now" or the new date is beyond <see cref="CalendarItem.RecurrenceEndsAtUtc"/>.
    /// </summary>
    public static CalendarItem? CreateNextInstance(CalendarItem finished, DateTime nowUtc)
    {
        if (!finished.IsRecurring || finished.RecurrenceIntervalHours is not > 0 || finished.DueUtc is not { } due)
        {
            return null;
        }

        var endsAtUtc = finished.RecurrenceEndsAtUtc;
        if (endsAtUtc.HasValue && nowUtc > endsAtUtc.Value)
        {
            return null;
        }

        var interval = TimeSpan.FromHours(finished.RecurrenceIntervalHours.Value);

        // Smallest number of intervals (>= 1) that moves the date past "now": floor(late / interval) + 1.
        var lateBy = nowUtc - due;
        var steps = lateBy > TimeSpan.Zero ? (long)Math.Floor(lateBy / interval) + 1 : 1;
        var shift = interval * steps;

        if (endsAtUtc.HasValue && due + shift > endsAtUtc.Value)
        {
            return null;
        }

        var isEvent = finished.TaskType == TaskType.Event;
        return new CalendarItem
        {
            TeamId = finished.TeamId,
            AssignedUserId = finished.AssignedUserId,
            Title = finished.Title,
            Description = finished.Description,
            Status = TaskStatusRules.DefaultFor(finished.TaskType),
            TaskType = finished.TaskType,
            // An event keeps its length: start and end move together.
            StartUtc = isEvent ? finished.StartUtc + shift : null,
            EndUtc = isEvent ? finished.EndUtc + shift : null,
            DeadlineUtc = isEvent ? null : finished.DeadlineUtc + shift,
            EstimatedEffortHours = finished.EstimatedEffortHours,
            ImpactScore = finished.ImpactScore,
            UseAiEnhancement = finished.UseAiEnhancement,
            // The successor carries the recurrence on, so the chain continues when it is finished in turn.
            IsRecurring = true,
            RecurrenceIntervalHours = finished.RecurrenceIntervalHours,
            RecurrenceEndsAtUtc = finished.RecurrenceEndsAtUtc
            // Deliberately not copied: blocker (belonged to that occurrence), manual urgency override,
            // and Google Calendar ids (the external event id is the sync's unique key).
        };
    }
}
