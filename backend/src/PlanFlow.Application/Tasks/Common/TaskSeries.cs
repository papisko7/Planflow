using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// Creating the occurrences of a repeating item (the rule itself is <see cref="RecurrenceRules"/>). An <b>event</b> series is kept
/// created about 90 days ahead, so every occurrence is a normal item people can see, edit and be reminded of; a <b>task</b> series has
/// one open occurrence at a time and gets the next one when that one is completed. New occurrences copy the series' "template", the
/// latest occurrence nobody changed on its own, so a one-off change to a single occurrence does not spread.
/// </summary>
public static class TaskSeries
{
    /// <summary>How far ahead the occurrences of an event series are created.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(90);

    /// <summary>A series is topped up once it reaches less than this far ahead, so the job works in batches (about every ten days) instead of every day.</summary>
    public static readonly TimeSpan RefillBelow = TimeSpan.FromDays(80);

    /// <summary>Occurrences further away than this are not rescored every hour (their score barely moves); they are scored when created and again once they get close.</summary>
    public static readonly TimeSpan ScoringWindow = TimeSpan.FromDays(14);

    /// <summary>
    /// Starts the series of <paramref name="item"/> (which becomes its first occurrence) from the repeat rule in <paramref name="fields"/>.
    /// The caller creates the further occurrences with <see cref="CreateAhead"/> once the item is complete.
    /// </summary>
    public static RecurrenceSeries Start(IApplicationDbContext context, CalendarItem item, ITaskScheduleFields fields, string? timeZoneId)
    {
        var anchor = item.DueUtc ?? throw new InvalidOperationException("A repeating item needs a start (event) or a deadline (task).");
        var series = new RecurrenceSeries
        {
            TeamId = item.TeamId,
            TaskType = item.TaskType,
            TimeZoneId = item.IsAllDay ? null : timeZoneId,
            AnchorUtc = anchor,
            GeneratedThroughUtc = anchor
        };
        SetRule(series, fields, item.IsAllDay);

        context.RecurrenceSeries.Add(series);
        item.RecurrenceSeriesId = series.Id;
        return series;
    }

    /// <summary>Puts the rule of the form onto the series. A weekly rule always stores its days, so two rules can be compared as they are.</summary>
    public static void SetRule(RecurrenceSeries series, ITaskScheduleFields fields, bool isAllDay)
    {
        series.Frequency = fields.RecurrenceFrequency ?? RecurrenceFrequency.Daily;
        series.Interval = Math.Clamp(fields.RecurrenceInterval ?? 1, 1, RecurrenceRules.MaxInterval);
        series.EndsAtUtc = fields.RecurrenceEndsAtUtc;
        series.Weekdays = 0;
        series.Weekdays = series.Frequency == RecurrenceFrequency.Weekly
            ? fields.RecurrenceWeekdays is > 0 ? fields.RecurrenceWeekdays.Value : RecurrenceRules.EffectiveWeekdays(series, isAllDay)
            : 0;
        series.UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>True when the rule in <paramref name="fields"/> is not the one the series has (or the item no longer repeats).</summary>
    public static bool RuleDiffers(RecurrenceSeries series, ITaskScheduleFields fields, bool isAllDay)
    {
        if (!fields.IsRecurring)
        {
            return true;
        }

        var weeklyDays = fields.RecurrenceWeekdays is > 0 ? fields.RecurrenceWeekdays.Value : RecurrenceRules.EffectiveWeekdays(series, isAllDay);
        return fields.RecurrenceFrequency != series.Frequency
            || (fields.RecurrenceInterval ?? 1) != series.Interval
            || fields.RecurrenceEndsAtUtc != series.EndsAtUtc
            || (series.Frequency == RecurrenceFrequency.Weekly && weeklyDays != RecurrenceRules.EffectiveWeekdays(series, isAllDay));
    }

    /// <summary>
    /// Creates the occurrences of an event series that fall after its cursor and within <see cref="Horizon"/> of <paramref name="nowUtc"/>, as
    /// copies of <paramref name="template"/> (its alerts and people included), and moves the cursor. Past occurrences are not made up
    /// afterwards: only the last day before "now" and later. A due moment that already has an occurrence is skipped.
    /// </summary>
    public static List<CalendarItem> CreateAhead(
        IApplicationDbContext context, RecurrenceSeries series, CalendarItem template, AlertsAndPeople carry, DateTime nowUtc,
        IReadOnlySet<DateTime>? existingDues = null)
    {
        var horizon = nowUtc + Horizon;
        var after = series.GeneratedThroughUtc > nowUtc.AddDays(-1) ? series.GeneratedThroughUtc : nowUtc.AddDays(-1);

        var created = new List<CalendarItem>();
        foreach (var due in RecurrenceRules.Occurrences(series, template.IsAllDay, after, horizon))
        {
            if (existingDues?.Contains(due) == true)
            {
                continue;
            }

            var copy = CloneAt(template, series, due);
            context.CalendarItems.Add(copy);
            carry.CopyTo(context, copy);
            created.Add(copy);
        }

        series.GeneratedThroughUtc = new[] { series.GeneratedThroughUtc, horizon }.Max();
        series.UpdatedAtUtc = nowUtc;
        return created;
    }

    /// <summary>
    /// The next occurrence of a task series after one was completed (or removed): the first date of the rule after both the old due date
    /// and "now", so nobody is handed an overdue copy. Null when the series has ended; the cursor then moves to its end, which closes it.
    /// </summary>
    public static CalendarItem? CreateNextTask(
        IApplicationDbContext context, RecurrenceSeries series, CalendarItem template, AlertsAndPeople carry, DateTime afterUtc, DateTime nowUtc)
    {
        var after = afterUtc > nowUtc ? afterUtc : nowUtc;
        var due = RecurrenceRules.NextAfter(series, template.IsAllDay, after);
        series.UpdatedAtUtc = nowUtc;
        if (due is null)
        {
            series.GeneratedThroughUtc = series.EndsAtUtc ?? after;
            return null;
        }

        var copy = CloneAt(template, series, due.Value);
        context.CalendarItems.Add(copy);
        carry.CopyTo(context, copy);
        series.GeneratedThroughUtc = due.Value;
        return copy;
    }

    /// <summary>
    /// The occurrence a new one is copied from: the latest that was not changed on its own, otherwise just the latest. Null when the series
    /// has no occurrences left. Its alerts and people are loaded, because the copies carry them on.
    /// </summary>
    public static async Task<CalendarItem?> LoadTemplateAsync(IApplicationDbContext context, Guid seriesId, CancellationToken cancellationToken)
    {
        var rows = await context.CalendarItems
            .Include(i => i.Reminders)
            .Include(i => i.Participants)
            .Where(i => i.RecurrenceSeriesId == seriesId)
            .ToListAsync(cancellationToken);
        return PickTemplate(rows);
    }

    public static CalendarItem? PickTemplate(IEnumerable<CalendarItem> occurrences)
    {
        var rows = occurrences.Where(i => i.DueUtc is not null).ToList();
        return rows.Where(i => !i.IsSeriesException).OrderByDescending(i => i.DueUtc).FirstOrDefault()
            ?? rows.OrderByDescending(i => i.DueUtc).FirstOrDefault();
    }

    /// <summary>
    /// A new occurrence of <paramref name="series"/> due at <paramref name="due"/>, copied from <paramref name="template"/> with all its dates
    /// moved by the same distance (so an event keeps its length and a task its planned start). Not copied: the blocker and the manual urgency
    /// (they belonged to that occurrence), the status (starts fresh) and anything Google-related.
    /// </summary>
    public static CalendarItem CloneAt(CalendarItem template, RecurrenceSeries series, DateTime due)
    {
        var isEvent = template.TaskType == TaskType.Event;
        var shift = due - template.DueUtc!.Value;
        return new CalendarItem
        {
            TeamId = template.TeamId,
            AssignedUserId = template.AssignedUserId,
            AssignedByUserId = template.AssignedByUserId,
            IsAssignmentLocked = template.IsAssignmentLocked,
            Title = template.Title,
            Description = template.Description,
            Status = TaskStatusRules.DefaultFor(template.TaskType),
            TaskType = template.TaskType,
            EventTemplate = template.EventTemplate,
            Location = template.Location,
            ColorKey = template.ColorKey,
            IsBusy = template.IsBusy,
            IsAllDay = template.IsAllDay,
            StartUtc = template.StartUtc + shift,
            EndUtc = isEvent ? template.EndUtc + shift : null,
            DeadlineUtc = isEvent ? null : template.DeadlineUtc + shift,
            EstimatedEffortHours = template.EstimatedEffortHours,
            ImpactScore = template.ImpactScore,
            UseAiEnhancement = template.UseAiEnhancement,
            ItemListId = template.ItemListId,
            RecurrenceSeriesId = series.Id
        };
    }

    /// <summary>The time zone a new series keeps its clock time in: the creator's own, otherwise UTC.</summary>
    public static async Task<string?> ZoneOfAsync(IApplicationDbContext context, Guid? userId, CancellationToken cancellationToken) =>
        userId is null
            ? null
            : await context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
}

/// <summary>The alerts and the people (guests / co-contractors) a new occurrence takes over from the one it is copied from.</summary>
public sealed record AlertsAndPeople(IReadOnlyList<ReminderInput> Reminders, IReadOnlyList<Guid> ParticipantIds)
{
    /// <summary>What <paramref name="item"/> has stored; it must have been loaded with its alerts and participants.</summary>
    public static AlertsAndPeople Of(CalendarItem item) => new(
        item.Reminders.Select(r => new ReminderInput(r.OffsetMinutes, r.DaysBefore, r.TimeOfDayMinutes, r.Anchor)).ToList(),
        item.Participants.Select(p => p.UserId).ToList());

    /// <summary>Gives the new occurrence the same alerts (fresh, not yet gone off) and people.</summary>
    public void CopyTo(IApplicationDbContext context, CalendarItem copy)
    {
        TaskReminders.Replace(context, copy, Reminders);
        TaskParticipants.Replace(context, copy, ParticipantIds);
    }
}
