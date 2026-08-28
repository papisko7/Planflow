using PlanFlow.Domain.Common;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// An application user. Password is stored as a hash; Google OAuth tokens live on <see cref="CalendarIntegration"/>.
/// </summary>
public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public ICollection<TeamMember> TeamMemberships { get; set; } = new List<TeamMember>();
    public ICollection<TaskItem> AssignedTasks { get; set; } = new List<TaskItem>();
    public ICollection<CalendarIntegration> CalendarIntegrations { get; set; } = new List<CalendarIntegration>();
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
    public ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
}
