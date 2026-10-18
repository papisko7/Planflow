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
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<CalendarItem> CalendarItems => Set<CalendarItem>();
    public DbSet<ItemReminder> ItemReminders => Set<ItemReminder>();
    public DbSet<TaskHistory> TaskHistories => Set<TaskHistory>();
    public DbSet<UrgencyScoreLog> UrgencyScoreLogs => Set<UrgencyScoreLog>();
    public DbSet<CalendarIntegration> CalendarIntegrations => Set<CalendarIntegration>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();
    public DbSet<OneTimeCode> OneTimeCodes => Set<OneTimeCode>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushToken> PushTokens => Set<PushToken>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanFlowDbContext).Assembly);
    }
}
