using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class ItemParticipantConfiguration : IEntityTypeConfiguration<ItemParticipant>
{
    public void Configure(EntityTypeBuilder<ItemParticipant> builder)
    {
        builder.ToTable("item_participants");
        builder.HasKey(p => p.Id);

        // A participant has no meaning without the item: deleting the item deletes its participants.
        builder.HasOne(p => p.CalendarItem)
            .WithMany(i => i.Participants)
            .HasForeignKey(p => p.CalendarItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // ... nor without the person: a deleted account simply stops taking part.
        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per person per item.
        builder.HasIndex(p => new { p.CalendarItemId, p.UserId }).IsUnique();
        builder.HasIndex(p => p.UserId);
    }
}
