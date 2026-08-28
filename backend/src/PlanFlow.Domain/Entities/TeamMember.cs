using PlanFlow.Domain.Common;
using PlanFlow.Domain.Enums;

namespace PlanFlow.Domain.Entities;

/// <summary>
/// Join entity linking a <see cref="User"/> to a <see cref="Team"/> with an RBAC role.
/// </summary>
public class TeamMember : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public TeamRole Role { get; set; } = TeamRole.Member;
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}
