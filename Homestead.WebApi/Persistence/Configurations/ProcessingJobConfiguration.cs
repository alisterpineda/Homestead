using Homestead.WebApi.Domain.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.ToTable("jobs", "processing");

        builder.HasKey(j => j.Id);

        builder.Property(j => j.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(j => j.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(j => j.EntityType)
            .HasConversion<string>();

        builder.Property(j => j.Payload)
            .HasColumnType("jsonb");

        // Staged handler output. jsonb like Payload — it holds serialized JSON (e.g. StagedStatementImport).
        builder.Property(j => j.Result)
            .HasColumnType("jsonb");

        builder.Property(j => j.Priority)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(j => j.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(j => j.TotalChildCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(j => j.CompletedChildCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(j => j.FailedChildCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(j => j.Attempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(j => j.MaxAttempts)
            .IsRequired()
            .HasDefaultValue(1);

        // RunAfter and LockedUntil need no configuration: nullable timestamptz. No index for them either —
        // the claim filters on Status first, and (Status, Priority) already narrows it to the live rows, a
        // tiny fraction of a table made almost entirely of terminal history.

        builder.HasOne<ProcessingJob>()
            .WithMany()
            .HasForeignKey(j => j.ParentJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProcessingSchedule>()
            .WithMany()
            .HasForeignKey(j => j.ScheduleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(j => j.Type);
        builder.HasIndex(j => j.EntityId);
        builder.HasIndex(j => new { j.Status, j.Priority });
        builder.HasIndex(j => j.CreatedByUserId);
        builder.HasIndex(j => j.ParentJobId);
        builder.HasIndex(j => new { j.ParentJobId, j.Status });
        builder.HasIndex(j => j.ScheduleId);
    }
}
