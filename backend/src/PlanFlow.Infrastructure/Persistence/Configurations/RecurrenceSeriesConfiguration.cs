using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class RecurrenceSeriesConfiguration : IEntityTypeConfiguration<RecurrenceSeries>
{
    public void Configure(EntityTypeBuilder<RecurrenceSeries> builder)
    {
        builder.ToTable("recurrence_series");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TimeZoneId).HasMaxLength(64);

        builder.HasIndex(s => s.GeneratedThroughUtc);
        builder.HasIndex(s => s.TeamId);

        builder.HasOne(s => s.Team)
            .WithMany()
            .HasForeignKey(s => s.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany<CalendarItem>()
            .WithOne(i => i.RecurrenceSeries)
            .HasForeignKey(i => i.RecurrenceSeriesId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
