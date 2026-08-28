using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts");
        builder.HasKey(a => a.Id);

        builder.HasOne(a => a.User)
            .WithMany(u => u.Alerts)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Keep the alert (e.g. "task X was deleted") even after the task itself is gone.
        builder.HasOne(a => a.TaskItem)
            .WithMany(t => t.Alerts)
            .HasForeignKey(a => a.TaskItemId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
