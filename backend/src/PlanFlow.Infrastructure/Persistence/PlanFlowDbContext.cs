using Microsoft.EntityFrameworkCore;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core gateway to PostgreSQL. Table shapes live in <c>Configurations/*</c>
/// (one <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}"/> per entity)
/// so this class stays a thin composition root. Implements <see cref="IApplicationDbContext"/> so
/// Application-layer MediatR handlers depend on the abstraction, not this concrete type.
/// </summary>
public class PlanFlowDbContext : DbContext, IApplicationDbContext
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
