using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(256);
        builder.Property(t => t.ExternalCalendarEventId).HasMaxLength(512);

        // Dashboard's main query is "give me this team's tasks sorted by urgency" — both filter
        // and sort columns are indexed so Postgres can serve it without a full table scan.
        builder.HasIndex(t => t.TeamId);
        builder.HasIndex(t => t.CurrentUrgencyScore)
            .IsDescending();

        // SyncCalendarJob's idempotency key: re-running a sync must find the same task by
        // (integration, Google event ID) instead of creating a duplicate. Filtered so
        // manually-created tasks (both columns null) never collide with each other.
        builder.HasIndex(t => new { t.SourceCalendarIntegrationId, t.ExternalCalendarEventId })
            .IsUnique()
            .HasFilter("\"SourceCalendarIntegrationId\" IS NOT NULL");

        builder.HasOne(t => t.Team)
            .WithMany(team => team.Tasks)
            .HasForeignKey(t => t.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // A synced task outlives the connection it was imported through: disconnecting Google
        // Calendar shouldn't delete the tasks it already created, just sever the link.
        builder.HasOne(t => t.SourceCalendarIntegration)
            .WithMany()
            .HasForeignKey(t => t.SourceCalendarIntegrationId)
            .OnDelete(DeleteBehavior.SetNull);

        // Assignment is a reference, not ownership: deleting the assignee unassigns the task
        // rather than deleting it.
        builder.HasOne(t => t.AssignedUser)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(t => t.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Self-reference for the "blocking" urgency component: deleting a blocker frees the
        // tasks it was blocking instead of deleting them too.
        builder.HasOne(t => t.BlockedByTask)
            .WithMany(t => t.BlockedTasks)
            .HasForeignKey(t => t.BlockedByTaskId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
