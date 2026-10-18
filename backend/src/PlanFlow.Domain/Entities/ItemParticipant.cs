using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// A team member who takes part in an item next to its assignee: a <see cref="ParticipantRole.Guest"/> of an event or a
/// <see cref="ParticipantRole.CoContractor"/> of a task. The assignee is never listed here. A participant hears about the item
/// (reminders, changes, deletion) and may leave it; only the assignee and Owner/Admin invite or remove people.
/// </summary>
public class ItemParticipant : BaseEntity
{
    /// <summary>At most this many people besides the assignee, so a reminder or a change never fans out to a whole company.</summary>
    public const int MaxPerItem = 10;

    public Guid CalendarItemId { get; set; }
    public CalendarItem CalendarItem { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public ParticipantRole Role { get; set; }

    /// <summary>The role an item of this type gives its participants: guests for an event, co-contractors for a task.</summary>
    public static ParticipantRole RoleFor(TaskType type) => type == TaskType.Event ? ParticipantRole.Guest : ParticipantRole.CoContractor;
}
