using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Services;

/// <summary>What a block of someone's personal calendar is, as far as a teammate may know.</summary>
public enum BlockKind
{
    /// <summary>Time taken by an event (or any event whose kind must not be revealed, e.g. a private one).</summary>
    Busy = 0,
    /// <summary>An event the person marked as not blocking their time (shown only with full details).</summary>
    Free = 1,
    OutOfOffice = 2,
    FocusTime = 3,
    WorkingLocation = 4,
    Birthday = 5
}

/// <summary>
/// One piece of a person's personal calendar after the sharing rules were applied. <see cref="StartUtc"/>/<see cref="EndUtc"/> are the
/// values as stored (an all-day block keeps its literal midnight-UTC dates); <see cref="Title"/> and <see cref="Location"/> are null
/// whenever the sharing level or Google's privacy flag forbids showing them.
/// </summary>
public record TimeBlock(Guid ItemId, BlockKind Kind, DateTime StartUtc, DateTime EndUtc, bool IsAllDay, string? Title, string? Location)
{
    /// <summary>True when the block takes the person's time (an event marked Free, a working location or a birthday does not).</summary>
    public bool BlocksTime => Kind is BlockKind.Busy or BlockKind.OutOfOffice or BlockKind.FocusTime;

    /// <summary>The real moments the block covers; an all-day block is read in the person's own time zone.</summary>
    public (DateTime Start, DateTime End) Interval(TimeZoneInfo zone) =>
        IsAllDay
            ? (AllDayTime.StartOfLocalDayUtc(StartUtc, zone), AllDayTime.StartOfLocalDayUtc(EndUtc, zone))
            : (StartUtc, EndUtc);
}

/// <summary>
/// The one place that decides what a teammate may see of someone's personal (imported) calendar. The server applies it, never the app,
/// so a hidden title never leaves the server. Rules: Off = nothing; Availability = the kind of block and its time, no text;
/// Details = also title and place. An event Google marks private/confidential - or one whose privacy is not known yet - is only ever "Busy".
/// </summary>
public static class PersonalCalendarSharing
{
    /// <summary>Assumed length of an event Google sent without an end.</summary>
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);

    /// <summary>The block a viewer may see for <paramref name="item"/>, or null when there is nothing to show.</summary>
    public static TimeBlock? BlockOf(CalendarItem item, CalendarSharingLevel level)
    {
        if (level == CalendarSharingLevel.Off || item.TaskType != TaskType.Event || item.StartUtc is null)
        {
            return null;
        }

        var start = item.StartUtc.Value;
        var end = item.EndUtc is { } e && e > start ? e : start + (item.IsAllDay ? TimeSpan.FromDays(1) : DefaultDuration);

        // Unknown privacy counts as private: a title must never leak because the flag has not been filled in yet.
        var isPrivate = item.IsPrivate != false;
        if (isPrivate)
        {
            return item.IsBusy ? new TimeBlock(item.Id, BlockKind.Busy, start, end, item.IsAllDay, null, null) : null;
        }

        var kind = KindOf(item);
        if (kind == BlockKind.Free && level != CalendarSharingLevel.Details)
        {
            return null; // "I am free then" is no use as an anonymous block
        }

        var details = level == CalendarSharingLevel.Details;
        return new TimeBlock(item.Id, kind, start, end, item.IsAllDay, details ? item.Title : null, details ? item.Location : null);
    }

    private static BlockKind KindOf(CalendarItem item) => item.EventTemplate switch
    {
        EventTemplate.OutOfOffice => BlockKind.OutOfOffice,
        EventTemplate.FocusTime => BlockKind.FocusTime,
        EventTemplate.WorkingLocation => BlockKind.WorkingLocation,
        EventTemplate.Birthday => BlockKind.Birthday,
        _ => item.IsBusy ? BlockKind.Busy : BlockKind.Free
    };
}
