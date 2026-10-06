using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title).IsRequired().HasMaxLength(512);
        builder.Property(n => n.Body).IsRequired().HasMaxLength(1000);
        builder.Property(n => n.PushLastError).HasMaxLength(500);

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The inbox lists one user's entries newest first; the unread count filters on ReadAtUtc.
        builder.HasIndex(n => new { n.UserId, n.CreatedAtUtc });

        // The push job repeatedly asks for "pending and due" rows.
        builder.HasIndex(n => new { n.PushState, n.PushNextAttemptAtUtc });
    }
}

public class PushTokenConfiguration : IEntityTypeConfiguration<PushToken>
{
    public void Configure(EntityTypeBuilder<PushToken> builder)
    {
        builder.ToTable("push_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Token).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Platform).IsRequired().HasMaxLength(20);

        // One phone address exists once; signing in as somebody else moves it (see RegisterPushTokenCommand).
        builder.HasIndex(t => t.Token).IsUnique();
        builder.HasIndex(t => t.UserId);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
