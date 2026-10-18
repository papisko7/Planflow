using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class ItemListConfiguration : IEntityTypeConfiguration<ItemList>
{
    public void Configure(EntityTypeBuilder<ItemList> builder)
    {
        builder.ToTable("item_lists");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name).IsRequired().HasMaxLength(ItemList.MaxNameLength);

        builder.HasIndex(l => l.TeamId);

        // A list belongs to its team and goes with it.
        builder.HasOne(l => l.Team)
            .WithMany()
            .HasForeignKey(l => l.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // Who made it is a reference: removing that account leaves the list, minus the "who".
        builder.HasOne(l => l.CreatedByUser)
            .WithMany()
            .HasForeignKey(l => l.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
