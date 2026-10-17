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

    /// <summary>When the owner proved they can read this address (null = not confirmed yet). Optional mails go only to confirmed addresses.</summary>
    public DateTime? EmailVerifiedAtUtc { get; set; }
    public bool IsEmailVerified => EmailVerifiedAtUtc is not null;

    public ICollection<TeamMember> TeamMemberships { get; set; } = new List<TeamMember>();
    public ICollection<CalendarItem> AssignedTasks { get; set; } = new List<CalendarItem>();
    public ICollection<CalendarIntegration> CalendarIntegrations { get; set; } = new List<CalendarIntegration>();
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
    public ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
}
