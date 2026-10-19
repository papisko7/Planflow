using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Exceptions;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Application.Tasks.Commands.UpdateTask;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>The fields whose change makes an occurrence an "exception" (changed on its own), taken before and after an edit.</summary>
public readonly record struct SeriesSnapshot(
    string Title, string? Description, DateTime? StartUtc, DateTime? EndUtc, DateTime? DeadlineUtc, string? Location, string? ColorKey,
    bool IsBusy, double? Effort, int? Impact, EventTemplate Template, TaskType Type, bool IsAllDay, Guid? AssigneeId)
{
    public static SeriesSnapshot Of(CalendarItem i) => new(
        i.Title, i.Description, i.StartUtc, i.EndUtc, i.DeadlineUtc, i.Location, i.ColorKey, i.IsBusy, i.EstimatedEffortHours,
        i.ImpactScore, i.EventTemplate, i.TaskType, i.IsAllDay, i.AssignedUserId);
}

/// <summary>Everything the series part of an edit needs to know about the edit that was just applied to <see cref="Task"/>.</summary>
/// <param name="Task">The occurrence the person opened, already updated with the request.</param>
/// <param name="Before">Its important fields as they were before the edit.</param>
/// <param name="OriginalDue">Its due moment before the edit (an edit may move it).</param>
/// <param name="AssigneeChanged">True when the edit changed who the item is assigned to; only then is the assignee passed on to the other occurrences.</param>
/// <param name="ParticipantIds">The guests / co-contractors as resolved for <see cref="Task"/>.</param>
/// <param name="Carry">Its alerts and people as saved, for the occurrences created from it.</param>
public sealed record SeriesEdit(
    UpdateTaskCommand Request, CalendarItem Task, RecurrenceSeries? Series, SeriesSnapshot Before, DateTime? OriginalDue,
    bool AssigneeChanged, IReadOnlyList<Guid> ParticipantIds, AlertsAndPeople Carry, string? ZoneId);

/// <summary>What the series part of an edit produced: the occurrences it created and the series the edited one belongs to now (null = none).</summary>
public sealed record SeriesEditResult(IReadOnlyList<CalendarItem> Created, RecurrenceSeries? Series);

/// <summary>
/// What editing or deleting one occurrence of a repeating item does to the others: the "This / This and following / All" choice.
/// <b>This</b> changes only the opened occurrence and marks it as an exception, so new occurrences do not copy it.
/// <b>All</b> passes the change to every occurrence. <b>This and following</b> cuts the series in two at the opened occurrence: the
/// earlier part keeps its old rule and ends where it was, the rest becomes a new series with the new rule, so a later "All" on
/// one half never touches the other.
/// A change of <i>day</i> moves only the occurrence it was made on; a change of time of day or length is passed on, and so is a change of the
/// repeat rule (the occurrences ahead that nobody finished are then made again from the new rule).
/// An imported (Google) series has no rule of ours, so only the fields PlanFlow adds (impact, effort, assignee, list) are passed on.
/// </summary>
public static class SeriesEditing
{
    /// <summary>Applies the series part of an edit of an app-created item. Returns the occurrences it created (to be scored) and the series the opened occurrence belongs to afterwards.</summary>
    public const string NotYoursMessage = "Only an Owner or Admin can change occurrences that are assigned to other people.";

    public static async Task<SeriesEditResult> ApplyAsync(
        IApplicationDbContext context, SeriesEdit edit, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var (request, task, series) = (edit.Request, edit.Task, edit.Series);

        if (series is null)
        {
            // Repeats switched on for an item that did not repeat: it becomes the first occurrence.
            if (!request.IsRecurring || task.DueUtc is null)
            {
                return new SeriesEditResult([], null);
            }

            var started = TaskSeries.Start(context, task, request, edit.ZoneId);
            var first = task.TaskType == TaskType.Event
                ? TaskSeries.CreateAhead(context, started, task, edit.Carry, nowUtc, new HashSet<DateTime> { task.DueUtc.Value })
                : [];
            return new SeriesEditResult(first, started);
        }

        if (edit.Request.Scope == SeriesScope.This)
        {
            if (SeriesSnapshot.Of(task) != edit.Before)
            {
                task.IsSeriesException = true;
            }

            return new SeriesEditResult([], series);
        }

        var others = await LoadOthersAsync(context, task, series.Id, null, cancellationToken);
        var scope = request.Scope;
        var earlier = edit.OriginalDue is { } was ? others.Where(o => o.DueUtc < was).ToList() : [];
        var following = others.Except(earlier).ToList();
        if (scope == SeriesScope.ThisAndFollowing && earlier.Count == 0)
        {
            scope = SeriesScope.All; // nothing before it to cut off
        }

        var affected = scope == SeriesScope.All ? others : following;
        EnsureMayChange(request, affected);
        foreach (var row in affected)
        {
            ApplyFields(context, row, edit, nowUtc);
        }
        task.IsSeriesException = false;

        var ruleChanged = TaskSeries.RuleDiffers(series, request, task.IsAllDay);
        var dueMoved = edit.OriginalDue != task.DueUtc;

        if (scope == SeriesScope.All)
        {
            series.TaskType = task.TaskType;
            if (!request.IsRecurring)
            {
                StopRepeating(context, series, task, others, nowUtc);
                return new SeriesEditResult([], null);
            }

            if (!ruleChanged)
            {
                return new SeriesEditResult([], series);
            }

            TaskSeries.SetRule(series, request, task.IsAllDay);
            var obsolete = others.Where(o => IsFutureAndOpen(o, nowUtc)).ToList();
            context.CalendarItems.RemoveRange(obsolete);
            return new SeriesEditResult(Regenerate(context, series, task, edit.Carry, others.Except(obsolete), nowUtc), series);
        }

        // This and following: the earlier part stops where it was, the rest continues as a series of its own.
        series.EndsAtUtc = earlier.Max(o => o.DueUtc);
        series.UpdatedAtUtc = nowUtc;
        if (!request.IsRecurring)
        {
            StopRepeating(context, null, task, following, nowUtc);
            return new SeriesEditResult([], null);
        }

        var tail = new RecurrenceSeries
        {
            TeamId = task.TeamId,
            TaskType = task.TaskType,
            TimeZoneId = series.TimeZoneId,
            AnchorUtc = task.DueUtc ?? edit.OriginalDue ?? series.AnchorUtc,
            GeneratedThroughUtc = series.GeneratedThroughUtc
        };
        TaskSeries.SetRule(tail, request, task.IsAllDay);
        context.RecurrenceSeries.Add(tail);
        task.RecurrenceSeriesId = tail.Id;
        foreach (var row in following)
        {
            row.RecurrenceSeriesId = tail.Id;
        }

        if (!ruleChanged && !dueMoved)
        {
            return new SeriesEditResult([], tail);
        }

        var obsoleteTail = following.Where(o => IsFutureAndOpen(o, nowUtc)).ToList();
        context.CalendarItems.RemoveRange(obsoleteTail);
        return new SeriesEditResult(Regenerate(context, tail, task, edit.Carry, following.Except(obsoleteTail), nowUtc), tail);
    }

    /// <summary>
    /// Applies the series part of an edit of an imported item. Google owns the series, so there is no rule to cut or change; the PlanFlow
    /// fields of the opened occurrence are passed to the ones the scope covers.
    /// </summary>
    public static async Task ApplyImportedAsync(IApplicationDbContext context, SeriesEdit edit, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var task = edit.Task;
        if (edit.Request.Scope == SeriesScope.This || task.SeriesId is null)
        {
            return;
        }

        var others = await LoadOthersAsync(context, task, null, task.SeriesId, cancellationToken);
        var covered = edit.Request.Scope == SeriesScope.All || edit.OriginalDue is null
            ? others
            : others.Where(o => o.DueUtc >= edit.OriginalDue).ToList();
        EnsureMayChange(edit.Request, covered);
        foreach (var row in covered)
        {
            row.EstimatedEffortHours = edit.Request.EstimatedEffortHours;
            row.ImpactScore = edit.Request.ImpactScore;
            row.UseAiEnhancement = edit.Request.UseAiEnhancement;
            row.ItemListId = task.ItemListId;
            row.IsDataIncomplete = false;
            row.UpdatedAtUtc = nowUtc;
            PassOnAssignee(row, edit);
        }
    }

    /// <summary>
    /// Deletes <paramref name="task"/> and, depending on <paramref name="scope"/>, the other occurrences the caller may delete
    /// (<paramref name="mayDelete"/>). Returns the occurrence created to stand in for a deleted open task, if any.
    /// </summary>
    public static async Task<CalendarItem?> DeleteAsync(
        IApplicationDbContext context, CalendarItem task, SeriesScope scope, Func<CalendarItem, bool> mayDelete, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (task.SeriesKey is null)
        {
            context.CalendarItems.Remove(task);
            return null;
        }

        var series = task.RecurrenceSeriesId is { } id ? await context.RecurrenceSeries.FirstOrDefaultAsync(s => s.Id == id, cancellationToken) : null;
        var others = await LoadOthersAsync(context, task, series?.Id, series is null ? task.SeriesId : null, cancellationToken);
        context.CalendarItems.Remove(task);

        if (scope == SeriesScope.This)
        {
            // Skipping the open occurrence of a repeating task brings the next one, otherwise the task would silently stop repeating.
            if (series is { TaskType: TaskType.Task } && !others.Any(o => !TaskStatusRules.IsFinished(o.Status)) && !TaskStatusRules.IsFinished(task.Status))
            {
                return TaskSeries.CreateNextTask(context, series, task, AlertsAndPeople.Of(task), task.DueUtc ?? nowUtc, nowUtc);
            }

            return null;
        }

        var due = task.DueUtc;
        var victims = (scope == SeriesScope.All || due is null ? others : others.Where(o => o.DueUtc >= due)).Where(mayDelete).ToList();
        context.CalendarItems.RemoveRange(victims);

        if (series is not null)
        {
            var remaining = others.Except(victims).Where(o => scope == SeriesScope.All || o.DueUtc < due).ToList();
            if (remaining.Count == 0)
            {
                context.RecurrenceSeries.Remove(series);
            }
            else if (scope == SeriesScope.ThisAndFollowing)
            {
                series.EndsAtUtc = remaining.Max(o => o.DueUtc); // what is left before it is all that remains of the series
                series.UpdatedAtUtc = nowUtc;
            }
        }

        return null;
    }

    /// <summary>A Member may pass a change on only to occurrences that are assigned to them; an Owner/Admin to any.</summary>
    private static void EnsureMayChange(UpdateTaskCommand request, IEnumerable<CalendarItem> rows)
    {
        if (!request.CallerIsManager && rows.Any(r => r.AssignedUserId != request.UpdatedByUserId))
        {
            throw new ForbiddenAccessException(NotYoursMessage);
        }
    }

    private static async Task<List<CalendarItem>> LoadOthersAsync(
        IApplicationDbContext context, CalendarItem task, Guid? seriesId, string? importedSeriesId, CancellationToken cancellationToken)
    {
        if (seriesId is null && importedSeriesId is null)
        {
            return [];
        }

        var query = context.CalendarItems
            .Include(i => i.Reminders)
            .Include(i => i.Participants)
            .Where(i => i.TeamId == task.TeamId && i.Id != task.Id);
        query = seriesId is { } sid ? query.Where(i => i.RecurrenceSeriesId == sid) : query.Where(i => i.SeriesId == importedSeriesId);
        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>Passes the edit of the opened occurrence on to another one of the series.</summary>
    private static void ApplyFields(IApplicationDbContext context, CalendarItem row, SeriesEdit edit, DateTime nowUtc)
    {
        var (request, task) = (edit.Request, edit.Task);
        var allDay = EventTemplateRules.ResolveAllDay(request.TaskType, request.EventTemplate, request.IsAllDay);
        var zone = allDay ? TimeZoneInfo.Utc : AllDayTime.ZoneOrUtc(edit.Series?.TimeZoneId ?? edit.ZoneId);
        var (start, end, deadline) = DatesFor(request, row, zone);

        TaskScheduleMapper.Apply(row, request with { StartUtc = start, EndUtc = end, DeadlineUtc = deadline });
        row.Title = request.Title;
        row.Status = TaskStatusRules.Normalize(row.TaskType, row.Status);
        row.ItemListId = task.ItemListId;
        row.UseAiEnhancement = request.UseAiEnhancement;
        row.ImpactScore = request.ImpactScore;
        row.IsSeriesException = false;
        row.UpdatedAtUtc = nowUtc;
        PassOnAssignee(row, edit);

        if (request.Reminders is not null)
        {
            TaskReminders.Replace(context, row, TaskReminders.Resolve(request.Reminders, row));
        }

        if (request.ParticipantUserIds is not null)
        {
            TaskParticipants.Replace(context, row, edit.ParticipantIds.Where(id => id != row.AssignedUserId).ToList());
        }
    }

    private static void PassOnAssignee(CalendarItem row, SeriesEdit edit)
    {
        if (!edit.AssigneeChanged)
        {
            return;
        }

        row.AssignedUserId = edit.Task.AssignedUserId;
        row.AssignedByUserId = edit.Task.AssignedByUserId;
        row.IsAssignmentLocked = edit.Task.IsAssignmentLocked;
    }

    /// <summary>
    /// The dates another occurrence gets from an edit: its own day, the edited occurrence's time of day and length. An all-day occurrence
    /// keeps its day and gets the edited span in days.
    /// </summary>
    private static (DateTime? Start, DateTime? End, DateTime? Deadline) DatesFor(UpdateTaskCommand request, CalendarItem row, TimeZoneInfo zone)
    {
        if (row.DueUtc is not { } rowDue)
        {
            return (request.StartUtc, request.EndUtc, request.DeadlineUtc);
        }

        if (request.TaskType == TaskType.Event)
        {
            if (request.StartUtc is not { } editedStart)
            {
                return (row.StartUtc, row.EndUtc, null);
            }

            var start = OnClock(rowDue, editedStart, zone);
            return (start, request.EndUtc is { } editedEnd ? start + (editedEnd - editedStart) : null, null);
        }

        if (request.DeadlineUtc is not { } editedDeadline)
        {
            return (row.StartUtc, null, row.DeadlineUtc);
        }

        var due = OnClock(rowDue, editedDeadline, zone);
        return (request.StartUtc is { } plannedStart ? due - (editedDeadline - plannedStart) : null, null, due);
    }

    /// <summary>The day of <paramref name="day"/> at the time of day of <paramref name="clock"/>, both read in <paramref name="zone"/>.</summary>
    private static DateTime OnClock(DateTime day, DateTime clock, TimeZoneInfo zone)
    {
        var date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(day, DateTimeKind.Utc), zone).Date;
        var timeOfDay = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(clock, DateTimeKind.Utc), zone).TimeOfDay;
        return RecurrenceRules.WallClockToUtc(date + timeOfDay, zone);
    }

    /// <summary>"Repeats" switched off: the occurrences ahead that nobody finished go, the ones already behind stay as ordinary items.</summary>
    private static void StopRepeating(
        IApplicationDbContext context, RecurrenceSeries? series, CalendarItem task, IEnumerable<CalendarItem> rows, DateTime nowUtc)
    {
        foreach (var row in rows)
        {
            if (IsFutureAndOpen(row, nowUtc))
            {
                context.CalendarItems.Remove(row);
            }
            else
            {
                row.RecurrenceSeriesId = null;
                row.IsSeriesException = false;
            }
        }

        task.RecurrenceSeriesId = null;
        task.IsSeriesException = false;
        if (series is not null)
        {
            context.RecurrenceSeries.Remove(series);
        }
    }

    /// <summary>Makes the occurrences of a changed series again from "now": whatever is still kept stays as it is, and the rest is created from the rule.</summary>
    private static IReadOnlyList<CalendarItem> Regenerate(
        IApplicationDbContext context, RecurrenceSeries series, CalendarItem template, AlertsAndPeople carry, IEnumerable<CalendarItem> kept, DateTime nowUtc)
    {
        if (template.TaskType != TaskType.Event)
        {
            return []; // a task series has one open occurrence; the next one follows the new rule when this one is completed
        }

        var dues = kept.Select(k => k.DueUtc).Where(d => d is not null).Select(d => d!.Value).Append(template.DueUtc!.Value).ToHashSet();
        series.GeneratedThroughUtc = nowUtc;
        return TaskSeries.CreateAhead(context, series, template, carry, nowUtc, dues);
    }

    private static bool IsFutureAndOpen(CalendarItem item, DateTime nowUtc) =>
        item.DueUtc > nowUtc && !TaskStatusRules.IsFinished(item.Status);
}
