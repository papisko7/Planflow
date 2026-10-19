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

        // The recurring job asks "which series still need occurrences?" every minute.
        builder.HasIndex(s => s.GeneratedThroughUtc);
        builder.HasIndex(s => s.TeamId);

        // A series belongs to its team and goes with it.
        builder.HasOne(s => s.Team)
            .WithMany()
            .HasForeignKey(s => s.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // Occurrences point at the rule; removing the rule (the user deleted the whole series) leaves any occurrence as a plain item.
        builder.HasMany<CalendarItem>()
            .WithOne(i => i.RecurrenceSeries)
            .HasForeignKey(i => i.RecurrenceSeriesId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
