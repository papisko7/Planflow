using Microsoft.EntityFrameworkCore;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Application.Common.Interfaces;

/// <summary>
/// Application-layer view of the EF Core database context. Declared here (not in Infrastructure)
/// so command/query handlers can depend on an abstraction instead of the concrete DbContext,
/// keeping the dependency direction Api/Infrastructure -&gt; Application -&gt; Domain intact.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Team> Teams { get; }
    DbSet<TeamMember> TeamMembers { get; }
    DbSet<TaskItem> Tasks { get; }
    DbSet<TaskHistory> TaskHistories { get; }
    DbSet<UrgencyScoreLog> UrgencyScoreLogs { get; }
    DbSet<Alert> Alerts { get; }
    DbSet<ChatMessage> ChatMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
