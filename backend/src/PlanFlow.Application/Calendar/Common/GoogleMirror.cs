using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Application.Calendar.Common;

/// <summary>
/// The way back to Google. An imported item that the person edits in the app is not sent to Google at once: the edit is only noted
/// (<see cref="Track"/> sets <see cref="CalendarItem.LocalEditedFields"/> and <see cref="CalendarItem.LocalEditedAtUtc"/>) and the next sync
/// sends it (<see cref="BuildEventPatch"/>, <see cref="BuildTaskPatch"/>). Pure functions, no I/O.
/// The conflict rule is last write wins, decided per item from the two "updated at" times (<see cref="GoogleIsNewer"/>).
/// </summary>
public static class GoogleMirror
{
    private static readonly TimeSpan DefaultEventLength = TimeSpan.FromHours(1);

    /// <summary>The fields of an item that also exist in Google, in a form that can be compared before and after an edit.</summary>
    public readonly record struct Snapshot(
        string Title, string? Description, DateTime? StartUtc, DateTime? EndUtc, bool IsAllDay, string? Location, string? ColorKey,
        bool IsBusy, DateTime? DeadlineUtc, bool IsCompleted, string Reminders);

    public static Snapshot Capture(CalendarItem item) => Capture(item, item.Reminders.Select(r => r.Key));

    /// <param name="reminderKeys">
    /// The item's reminders. Passed in because an edit replaces them through the database context, which leaves the item's own collection
    /// behind until the save.
    /// </param>
    public static Snapshot Capture(CalendarItem item, IEnumerable<(ReminderAnchor Anchor, int? Offset, int? Days, int? TimeOfDay)> reminderKeys) => new(
        item.Title, item.Description, item.StartUtc, item.EndUtc, item.IsAllDay, item.Location, item.ColorKey, item.IsBusy,
        item.DeadlineUtc, item.Status == TaskStatus.Completed,
        string.Join(';', reminderKeys.Select(k => $"{(int)k.Anchor}:{k.Offset}:{k.Days}:{k.TimeOfDay}").Order(StringComparer.Ordinal)));

    /// <summary>
    /// Whether an edit of this item can reach Google. A Google task always can; an event can unless Google manages it itself
    /// (a birthday, a working location), where it takes no changes.
    /// </summary>
    public static bool IsPushable(CalendarItem item) =>
        item.IsGoogleTask
        || (item.ExternalCalendarEventId is not null
            && item.EventTemplate is EventTemplate.Default or EventTemplate.OutOfOffice or EventTemplate.FocusTime);

    /// <summary>The fields that differ between two snapshots of the same item and exist in Google for its kind.</summary>
    public static MirroredFields Diff(Snapshot before, Snapshot after, bool isGoogleTask)
    {
        var changed = MirroredFields.None;
        if (before.Title != after.Title)
        {
            changed |= MirroredFields.Title;
        }
        if (Blank(before.Description) != Blank(after.Description))
        {
            changed |= MirroredFields.Description;
        }

        if (isGoogleTask)
        {
            if (before.DeadlineUtc?.Date != after.DeadlineUtc?.Date)
            {
                changed |= MirroredFields.Due;
            }
            if (before.IsCompleted != after.IsCompleted)
            {
                changed |= MirroredFields.Completed;
            }

            return changed;
        }

        if (before.StartUtc != after.StartUtc || before.EndUtc != after.EndUtc || before.IsAllDay != after.IsAllDay)
        {
            changed |= MirroredFields.Time;
        }
        if (Blank(before.Location) != Blank(after.Location))
        {
            changed |= MirroredFields.Location;
        }
        if (before.ColorKey != after.ColorKey)
        {
            changed |= MirroredFields.Color;
        }
        if (before.IsBusy != after.IsBusy)
        {
            changed |= MirroredFields.Busy;
        }
        if (before.Reminders != after.Reminders)
        {
            changed |= MirroredFields.Reminders;
        }

        return changed;
    }

    /// <summary>
    /// Notes an edit of an imported item: the fields that changed since <paramref name="before"/> are added to those waiting for the next
    /// sync, and the moment of the edit becomes the "updated at" of the app's version. An item Google takes no changes for is left alone.
    /// </summary>
    public static void Track(CalendarItem item, Snapshot before, Snapshot after, DateTime nowUtc)
    {
        if (!IsPushable(item))
        {
            return;
        }

        var changed = Diff(before, after, item.IsGoogleTask);
        if (changed == MirroredFields.None)
        {
            return;
        }

        item.LocalEditedFields |= changed;
        item.LocalEditedAtUtc = nowUtc;
    }

    /// <summary>The app's edits no longer wait: they were sent, or Google's version replaced them.</summary>
    public static void Settle(CalendarItem item)
    {
        item.LocalEditedFields = MirroredFields.None;
        item.LocalEditedAtUtc = null;
    }

    /// <summary>
    /// Last write wins: whether Google's version of an item is newer than the edit waiting in the app. Without Google's time there is
    /// nothing to prove the app's edit is newer, so Google wins; a tie goes to the app.
    /// </summary>
    public static bool GoogleIsNewer(DateTime? googleUpdatedUtc, DateTime? localEditedAtUtc) =>
        googleUpdatedUtc is null || localEditedAtUtc is null || googleUpdatedUtc > localEditedAtUtc;

    /// <summary>
    /// What to change in the Google event so it matches the fields of <paramref name="item"/> that wait in <see cref="CalendarItem.LocalEditedFields"/>.
    /// Only parts that really differ from <paramref name="current"/> are included; null = nothing to send.
    /// </summary>
    public static GoogleEventPatch? BuildEventPatch(CalendarItem item, GoogleCalendarEvent current)
    {
        var fields = item.LocalEditedFields;
        var rules = EventTemplateRules.For(item.TaskType, item.EventTemplate);
        string? title = null, description = null, location = null, colorId = null;
        GoogleEventTime? time = null;
        bool? isFree = null;
        IReadOnlyList<int>? reminders = null;

        if (fields.HasFlag(MirroredFields.Title) && item.Title != TitleOf(current.Title))
        {
            title = PlanflowTagParser.TitleWithTagOf(item.Title, current.Title);
        }

        if (fields.HasFlag(MirroredFields.Description) && rules.AllowsDescription && Blank(item.Description) != DescriptionOf(current.Description))
        {
            description = PlanflowTagParser.DescriptionWithTagOf(item.Description, current.Description);
        }

        if (fields.HasFlag(MirroredFields.Time) && item.StartUtc is { } start)
        {
            // Google needs an end; an item without one gets the length the ranking assumes (an all-day item, one day).
            var end = item.EndUtc ?? (item.IsAllDay ? start.AddDays(1) : start + DefaultEventLength);
            if (start != current.StartUtc || end != current.EndUtc || item.IsAllDay != current.IsAllDay)
            {
                time = new GoogleEventTime(start, end, item.IsAllDay);
            }
        }

        if (fields.HasFlag(MirroredFields.Location) && rules.Location == LocationMode.Text
            && Blank(item.Location) != Blank(current.Location?.Trim()))
        {
            location = item.Location ?? string.Empty;
        }

        if (fields.HasFlag(MirroredFields.Color) && rules.AllowsColor
            && ItemColors.TryToGoogleColorId(item.ColorKey, out var id) && (id ?? string.Empty) != (current.ColorId ?? string.Empty))
        {
            colorId = id;
        }

        if (fields.HasFlag(MirroredFields.Busy) && rules.Busy == BusyMode.Choice && current.IsFree == item.IsBusy)
        {
            isFree = !item.IsBusy;
        }

        if (fields.HasFlag(MirroredFields.Reminders) && rules.AllowsAlerts)
        {
            var minutes = GoogleReminderMapper.ToMinutes(item.Reminders.Select(r => r.Key));
            if (current.ReminderMinutes is null || !minutes.SequenceEqual(current.ReminderMinutes.Distinct().Order()))
            {
                reminders = minutes;
            }
        }

        var patch = new GoogleEventPatch(title, description, time, location, colorId, isFree, reminders);
        return patch.IsEmpty ? null : patch;
    }

    /// <summary>What to change in the Google task so it matches the fields of <paramref name="item"/> that wait to be sent; null = nothing to send.</summary>
    public static GoogleTaskPatch? BuildTaskPatch(CalendarItem item, GoogleTask current)
    {
        var fields = item.LocalEditedFields;
        string? title = null, notes = null;
        DateTime? due = null;
        bool? completed = null;

        if (fields.HasFlag(MirroredFields.Title) && item.Title != TitleOf(current.Title))
        {
            title = PlanflowTagParser.TitleWithTagOf(item.Title, current.Title);
        }

        if (fields.HasFlag(MirroredFields.Description) && Blank(item.Description) != DescriptionOf(current.Notes))
        {
            notes = PlanflowTagParser.DescriptionWithTagOf(item.Description, current.Notes);
        }

        if (fields.HasFlag(MirroredFields.Due) && item.DeadlineUtc is { } deadline && deadline.Date != current.DueDay?.Date)
        {
            due = DateTime.SpecifyKind(deadline.Date, DateTimeKind.Utc);
        }

        if (fields.HasFlag(MirroredFields.Completed) && (item.Status == TaskStatus.Completed) != current.IsCompleted)
        {
            completed = item.Status == TaskStatus.Completed;
        }

        var patch = new GoogleTaskPatch(title, notes, due, completed);
        return patch.IsEmpty ? null : patch;
    }

    /// <summary>The title as the app stores it: Google's without its <c>[planflow]</c> part.</summary>
    private static string TitleOf(string? googleTitle) =>
        string.IsNullOrWhiteSpace(googleTitle) ? string.Empty : PlanflowTagParser.StripFromTitle(googleTitle);

    /// <summary>The description as the app stores it: Google's as plain text without its <c>[planflow]</c> line.</summary>
    private static string? DescriptionOf(string? googleDescription) =>
        PlanflowTagParser.StripTagLines(PlanflowTagParser.ToPlainText(googleDescription));

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
