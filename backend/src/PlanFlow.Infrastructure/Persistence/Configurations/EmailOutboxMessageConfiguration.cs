using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("email_outbox");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.ToEmail).IsRequired().HasMaxLength(256);
        builder.Property(m => m.Subject).IsRequired().HasMaxLength(512);
        builder.Property(m => m.LastError).HasMaxLength(1000);

        builder.HasIndex(m => new { m.SentAtUtc, m.NextAttemptAtUtc });
    }
}
