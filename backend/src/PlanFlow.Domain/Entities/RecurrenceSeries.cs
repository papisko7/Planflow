using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// The repeat rule of an item created in the app, shared by all its occurrences (each occurrence is a normal <see cref="CalendarItem"/>
/// that points here). Holds the rule ("every 2 weeks on Mon and Wed, until ..."), where it starts, and a cursor that says how far
/// occurrences have been created. Events are created ahead (see <see cref="Services.RecurrenceRules"/> and RecurringItemJob),
/// a task gets its next occurrence when the previous one is completed.
/// </summary>
public class RecurrenceSeries : BaseEntity
{
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    /// <summary>What the occurrences are: events are created ahead of time, a task only when its predecessor is completed.</summary>
    public TaskType TaskType { get; set; }

    public RecurrenceFrequency Frequency { get; set; }

    /// <summary>"Every N" units of <see cref="Frequency"/>, at least 1.</summary>
    public int Interval { get; set; } = 1;

    /// <summary>
    /// Weekly only: the days of the week as a bit mask (Monday = 1 ... Sunday = 64, see <see cref="Services.RecurrenceRules.Weekdays"/>).
    /// 0 = the weekday of <see cref="AnchorUtc"/>. Ignored by the other frequencies.
    /// </summary>
    public int Weekdays { get; set; }

    /// <summary>The first occurrence (an event's start, a task's deadline). Monthly / yearly rules repeat its day of the month; every rule repeats its time of day.</summary>
    public DateTime AnchorUtc { get; set; }

    /// <summary>Last moment an occurrence may fall on (inclusive); null = never ends.</summary>
    public DateTime? EndsAtUtc { get; set; }

    /// <summary>
    /// IANA zone in which a timed occurrence keeps its clock time (a 09:00 meeting stays 09:00 after the clocks change). The zone of the
    /// person who created the series; null = UTC. Irrelevant for all-day items, whose day is read literally.
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// Every occurrence due on or before this moment has been created, or was deliberately removed by the user, so it is never created
    /// again. For an event series the job keeps it about 90 days ahead; for a task series it is the due moment of the last occurrence created.
    /// </summary>
    public DateTime GeneratedThroughUtc { get; set; }
}
