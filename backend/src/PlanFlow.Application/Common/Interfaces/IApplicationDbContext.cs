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
    DbSet<RefreshSession> RefreshSessions { get; }
    DbSet<Team> Teams { get; }
    DbSet<TeamMember> TeamMembers { get; }
    DbSet<CalendarItem> CalendarItems { get; }
    DbSet<ItemReminder> ItemReminders { get; }
    DbSet<ItemParticipant> ItemParticipants { get; }
    DbSet<TaskHistory> TaskHistories { get; }
    DbSet<UrgencyScoreLog> UrgencyScoreLogs { get; }
    DbSet<Alert> Alerts { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<CalendarIntegration> CalendarIntegrations { get; }
    DbSet<ExternalTaskList> ExternalTaskLists { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<EmailOutboxMessage> EmailOutbox { get; }
    DbSet<OneTimeCode> OneTimeCodes { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<PushToken> PushTokens { get; }
    DbSet<UserDevice> UserDevices { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
