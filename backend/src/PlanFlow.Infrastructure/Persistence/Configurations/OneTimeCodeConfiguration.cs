using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class OneTimeCodeConfiguration : IEntityTypeConfiguration<OneTimeCode>
{
    public void Configure(EntityTypeBuilder<OneTimeCode> builder)
    {
        builder.ToTable("one_time_codes");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CodeHash).IsRequired();
        builder.HasIndex(c => c.UserId);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
