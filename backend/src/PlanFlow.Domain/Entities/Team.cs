using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// A group of users collaborating on a shared set of tasks.
/// </summary>
public class Team : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// True for the one-person workspace every user gets automatically. It lets a user manage their own
    /// tasks without creating a team, while reusing all team machinery (permissions, caching, sync).
    /// A personal team never gets extra members or a chat.
    /// </summary>
    public bool IsPersonal { get; set; }

    public ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
    public ICollection<CalendarItem> CalendarItems { get; set; } = new List<CalendarItem>();
    public ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
}
