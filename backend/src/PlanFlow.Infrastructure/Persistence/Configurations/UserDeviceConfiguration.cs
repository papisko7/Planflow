using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> builder)
    {
        builder.ToTable("user_devices");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DeviceId).IsRequired().HasMaxLength(128);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(128);

        builder.HasIndex(d => new { d.UserId, d.DeviceId }).IsUnique();

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
