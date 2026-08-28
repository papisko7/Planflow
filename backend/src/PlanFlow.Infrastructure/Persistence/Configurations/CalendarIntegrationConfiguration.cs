using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class CalendarIntegrationConfiguration : IEntityTypeConfiguration<CalendarIntegration>
{
    public void Configure(EntityTypeBuilder<CalendarIntegration> builder)
    {
        builder.ToTable("calendar_integrations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ExternalAccountId).IsRequired();

        // The sync job's idempotency key: re-running a sync for the same external account
        // must upsert, never duplicate the link.
        builder.HasIndex(c => c.ExternalAccountId).IsUnique();

        builder.HasOne(c => c.User)
            .WithMany(u => u.CalendarIntegrations)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
