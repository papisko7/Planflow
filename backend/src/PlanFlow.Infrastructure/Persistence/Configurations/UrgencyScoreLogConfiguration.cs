using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class UrgencyScoreLogConfiguration : IEntityTypeConfiguration<UrgencyScoreLog>
{
    public void Configure(EntityTypeBuilder<UrgencyScoreLog> builder)
    {
        builder.ToTable("urgency_score_logs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.ReasonText).HasMaxLength(512);

        builder.HasIndex(l => new { l.CalendarItemId, l.CreatedAtUtc });

        builder.HasOne(l => l.CalendarItem)
            .WithMany(t => t.UrgencyScoreLogs)
            .HasForeignKey(l => l.CalendarItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
