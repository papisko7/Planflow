using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// A list of the team's own that groups items: a <see cref="ItemListKind.TaskList"/> holds tasks, a <see cref="ItemListKind.MeetingList"/>
/// holds events. Separate from the read-only <see cref="ExternalTaskList"/>s mirrored from Google Tasks. Deleting a list never deletes
/// the items in it; they just lose the grouping.
/// </summary>
public class ItemList : BaseEntity
{
    public const int MaxNameLength = 100;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public ItemListKind Kind { get; set; }

    /// <summary>Who made the list: the creator and Owner/Admin may rename or delete it. Null once that account is gone.</summary>
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    /// <summary>The kind of list an item of this type can sit in: tasks in task lists, events in meeting lists.</summary>
    public static ItemListKind KindFor(TaskType type) => type == TaskType.Event ? ItemListKind.MeetingList : ItemListKind.TaskList;
}
