using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class UrgencyScoreLogConfiguration : IEntityTypeConfiguration<UrgencyScoreLog>
{
    public void Configure(EntityTypeBuilder<UrgencyScoreLog> builder)
    {
        builder.ToTable("urgency_score_logs");
        builder.HasKey(l => l.Id);

        // Evaluation-chapter charts read "score history for task X over time".
        builder.HasIndex(l => new { l.TaskItemId, l.CreatedAtUtc });

        builder.HasOne(l => l.TaskItem)
            .WithMany(t => t.UrgencyScoreLogs)
            .HasForeignKey(l => l.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
