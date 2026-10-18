using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class ItemReminderConfiguration : IEntityTypeConfiguration<ItemReminder>
{
    public void Configure(EntityTypeBuilder<ItemReminder> builder)
    {
        builder.ToTable("item_reminders");
        builder.HasKey(r => r.Id);

        // A reminder has no meaning without its item: deleting the item deletes its reminders.
        builder.HasOne(r => r.CalendarItem)
            .WithMany(i => i.Reminders)
            .HasForeignKey(r => r.CalendarItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.CalendarItemId);

        // Exactly one form is filled: the offset ("30 min before") or the day form ("1 day before at 09:00").
        // The explicit IS NOT NULL on the time of day matters: a CHECK passes when its result is NULL, and NULL BETWEEN ... is NULL.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_item_reminders_one_form",
            "(\"OffsetMinutes\" IS NOT NULL AND \"DaysBefore\" IS NULL AND \"TimeOfDayMinutes\" IS NULL) OR " +
            "(\"OffsetMinutes\" IS NULL AND \"DaysBefore\" IS NOT NULL AND \"TimeOfDayMinutes\" IS NOT NULL AND \"TimeOfDayMinutes\" BETWEEN 0 AND 1439)"));
    }
}
