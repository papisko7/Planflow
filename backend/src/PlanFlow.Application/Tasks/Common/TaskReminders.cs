using FluentValidation;
using FluentValidation.Results;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;

namespace PlanFlow.Application.Tasks.Common;

/// <summary>
/// One reminder as the form sends it. Offset form: <see cref="OffsetMinutes"/> before the anchor. Day form: <see cref="DaysBefore"/> whole
/// days earlier at <see cref="TimeOfDayMinutes"/> after midnight. Exactly one form is filled; <see cref="Anchor"/> is what it counts back from.
/// </summary>
public record ReminderInput(int? OffsetMinutes, int? DaysBefore, int? TimeOfDayMinutes, ReminderAnchor Anchor = ReminderAnchor.Start)
{
    public ItemReminder ToEntity() => new()
    {
        Anchor = Anchor,
        OffsetMinutes = OffsetMinutes,
        DaysBefore = DaysBefore,
        TimeOfDayMinutes = TimeOfDayMinutes
    };
}

/// <summary>
/// The rules for an item's reminders and the way they are saved. Two kinds of rule: the shape of one reminder (<see cref="Problem"/>,
/// needs nothing else) and what the item can carry (<see cref="Resolve"/>, needs the item as it will be stored).
/// </summary>
public static class TaskReminders
{
    /// <summary>What is wrong with one reminder on its own, or null: not exactly one form, or a number out of range.</summary>
    public static string? Problem(ReminderInput reminder)
    {
        var isOffset = reminder.OffsetMinutes is not null;
        var isDay = reminder.DaysBefore is not null || reminder.TimeOfDayMinutes is not null;
        if (isOffset == isDay)
        {
            return "An alert is either 'N minutes before' or 'N days before at a time of day'.";
        }

        if (!Enum.IsDefined(reminder.Anchor))
        {
            return "Unknown alert anchor.";
        }

        if (isOffset)
        {
            return reminder.OffsetMinutes is >= 0 and <= ItemReminder.MaxOffsetMinutes
                ? null
                : $"An alert can be at most {ItemReminder.MaxOffsetMinutes / (24 * 60)} days before.";
        }

        if (reminder.DaysBefore is null || reminder.TimeOfDayMinutes is null)
        {
            return "A day alert needs both the number of days and the time of day.";
        }

        if (reminder.DaysBefore is < 0 or > ItemReminder.MaxDaysBefore)
        {
            return $"An alert can be at most {ItemReminder.MaxDaysBefore} days before.";
        }

        return reminder.TimeOfDayMinutes is >= 0 and < 24 * 60 ? null : "The time of day of an alert must be between 00:00 and 23:59.";
    }

    /// <summary>
    /// The list as it will be stored for <paramref name="item"/> (already carrying the type, template, dates and all-day flag of the save).
    /// A kind of item without alerts (Out of office, Working location) keeps none, like every field its template lacks. Otherwise the list
    /// must make sense for this very item, and a mistake is a validation error the form shows.
    /// </summary>
    public static IReadOnlyList<ReminderInput> Resolve(IReadOnlyList<ReminderInput> requested, CalendarItem item)
    {
        if (!EventTemplateRules.For(item.TaskType, item.EventTemplate).AllowsAlerts)
        {
            return [];
        }

        if (requested.Count > ItemReminder.MaxPerItem)
        {
            throw Invalid($"An item can have at most {ItemReminder.MaxPerItem} alerts.");
        }

        foreach (var reminder in requested)
        {
            if (Problem(reminder) is { } problem)
            {
                throw Invalid(problem);
            }

            if (reminder.Anchor == ReminderAnchor.Deadline && item.TaskType != TaskType.Task)
            {
                throw Invalid("Only a task has a deadline to be reminded before.");
            }

            var anchorMoment = reminder.Anchor == ReminderAnchor.Deadline ? item.DeadlineUtc : item.StartUtc;
            if (anchorMoment is null)
            {
                throw Invalid(reminder.Anchor == ReminderAnchor.Deadline
                    ? "Set a deadline first: an alert counts back from it."
                    : "Set a start first: an alert counts back from it.");
            }

            // "Minutes before midnight" is not something a person thinks in, so a whole-day item is only reminded some days before, at a time of day.
            if (item.IsAllDay && reminder.OffsetMinutes is not null)
            {
                throw Invalid("An all-day item can only be reminded some days before, at a time of day.");
            }
        }

        if (requested.Select(r => (r.Anchor, r.OffsetMinutes, r.DaysBefore, r.TimeOfDayMinutes)).Distinct().Count() != requested.Count)
        {
            throw Invalid("Two alerts are the same.");
        }

        return requested;
    }

    /// <summary>
    /// Makes the stored reminders of <paramref name="item"/> equal to <paramref name="wanted"/>. A reminder that stays is left alone (so it
    /// does not fire again); the rest are added or removed. The item must have been loaded with its <see cref="CalendarItem.Reminders"/>
    /// (a new item has none yet).
    /// </summary>
    public static void Replace(IApplicationDbContext context, CalendarItem item, IReadOnlyList<ReminderInput> wanted)
    {
        var wantedKeys = wanted.Select(w => w.ToEntity().Key).ToHashSet();

        foreach (var stored in item.Reminders.Where(r => !wantedKeys.Contains(r.Key)).ToList())
        {
            context.ItemReminders.Remove(stored);
        }

        var storedKeys = item.Reminders.Select(r => r.Key).ToHashSet();
        foreach (var input in wanted)
        {
            var reminder = input.ToEntity();
            if (storedKeys.Contains(reminder.Key))
            {
                continue;
            }

            reminder.CalendarItemId = item.Id;
            context.ItemReminders.Add(reminder);
        }
    }

    private static ValidationException Invalid(string message) =>
        new(new[] { new ValidationFailure("Reminders", message) });
}
