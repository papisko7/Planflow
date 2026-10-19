using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class CalendarItemConfiguration : IEntityTypeConfiguration<CalendarItem>
{
    public void Configure(EntityTypeBuilder<CalendarItem> builder)
    {
        builder.ToTable("calendar_items");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(256);
        builder.Property(t => t.ExternalCalendarEventId).HasMaxLength(512);
        builder.Property(t => t.Location).HasMaxLength(512);
        builder.Property(t => t.ColorKey).HasMaxLength(32);
        builder.Property(t => t.SeriesId).HasMaxLength(512);
        builder.Property(t => t.ExternalStatus).HasMaxLength(32);
        builder.Property(t => t.ExternalDescriptionHash).HasMaxLength(64); // hex SHA-256
        builder.Property(t => t.ExternalSettings).HasMaxLength(1000);
        builder.Property(t => t.ExternalTaskId).HasMaxLength(512);

        // Dashboard's main query is "give me this team's tasks sorted by urgency" — both filter
        // and sort columns are indexed so Postgres can serve it without a full table scan.
        builder.HasIndex(t => t.TeamId);
        builder.HasIndex(t => t.RecurrenceSeriesId); // "all occurrences of this series" (series pop-up, recurring job)
        builder.HasIndex(t => t.CurrentUrgencyScore)
            .IsDescending();

        // SyncCalendarJob's idempotency key: re-running a sync must find the same item by
        // (integration, Google event ID) instead of creating a duplicate. Filtered so
        // items without a Google event (manual ones and imported Google tasks) never collide with each other.
        builder.HasIndex(t => new { t.SourceCalendarIntegrationId, t.ExternalCalendarEventId })
            .IsUnique()
            .HasFilter("\"ExternalCalendarEventId\" IS NOT NULL");

        builder.HasIndex(t => new { t.SourceCalendarIntegrationId, t.ExternalTaskId })
            .IsUnique()
            .HasFilter("\"ExternalTaskId\" IS NOT NULL");

        builder.HasOne(t => t.ExternalTaskList)
            .WithMany()
            .HasForeignKey(t => t.ExternalTaskListId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.ItemList)
            .WithMany()
            .HasForeignKey(t => t.ItemListId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.Team)
            .WithMany(team => team.CalendarItems)
            .HasForeignKey(t => t.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.SourceCalendarIntegration)
            .WithMany()
            .HasForeignKey(t => t.SourceCalendarIntegrationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.AssignedUser)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(t => t.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.AssignedByUser)
            .WithMany()
            .HasForeignKey(t => t.AssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.BlockedByTask)
            .WithMany(t => t.BlockedTasks)
            .HasForeignKey(t => t.BlockedByTaskId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
