using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class ExternalTaskListConfiguration : IEntityTypeConfiguration<ExternalTaskList>
{
    public void Configure(EntityTypeBuilder<ExternalTaskList> builder)
    {
        builder.ToTable("external_task_lists");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.ExternalId).IsRequired().HasMaxLength(512);
        builder.Property(l => l.Title).IsRequired().HasMaxLength(256);

        // The sync finds a list by (account, Google's id): re-running it updates the same row instead of adding a copy.
        builder.HasIndex(l => new { l.CalendarIntegrationId, l.ExternalId }).IsUnique();

        builder.HasOne(l => l.CalendarIntegration)
            .WithMany()
            .HasForeignKey(l => l.CalendarIntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
