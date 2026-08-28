using Microsoft.EntityFrameworkCore;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core gateway to PostgreSQL. Table shapes live in <c>Configurations/*</c>
/// (one <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}"/> per entity)
/// so this class stays a thin composition root.
/// </summary>
public class PlanFlowDbContext : DbContext
{
    public PlanFlowDbContext(DbContextOptions<PlanFlowDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskHistory> TaskHistories => Set<TaskHistory>();
    public DbSet<UrgencyScoreLog> UrgencyScoreLogs => Set<UrgencyScoreLog>();
    public DbSet<CalendarIntegration> CalendarIntegrations => Set<CalendarIntegration>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanFlowDbContext).Assembly);
    }
}
