using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanFlow.Domain.Entities;

namespace PlanFlow.Infrastructure.Persistence.Configurations;

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(256);

        // Dashboard's main query is "give me this team's tasks sorted by urgency" — both filter
        // and sort columns are indexed so Postgres can serve it without a full table scan.
        builder.HasIndex(t => t.TeamId);
        builder.HasIndex(t => t.CurrentUrgencyScore)
            .IsDescending();

        builder.HasOne(t => t.Team)
            .WithMany(team => team.Tasks)
            .HasForeignKey(t => t.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // Assignment is a reference, not ownership: deleting the assignee unassigns the task
        // rather than deleting it.
        builder.HasOne(t => t.AssignedUser)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(t => t.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Self-reference for the "blocking" urgency component: deleting a blocker frees the
        // tasks it was blocking instead of deleting them too.
        builder.HasOne(t => t.BlockedByTask)
            .WithMany(t => t.BlockedTasks)
            .HasForeignKey(t => t.BlockedByTaskId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
