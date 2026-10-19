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

        builder.HasOne(p => p.CalendarItem)
            .WithMany(i => i.Participants)
            .HasForeignKey(p => p.CalendarItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.CalendarItemId, p.UserId }).IsUnique();
        builder.HasIndex(p => p.UserId);
    }
}
