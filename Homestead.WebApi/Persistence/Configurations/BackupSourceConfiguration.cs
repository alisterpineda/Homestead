using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class BackupSourceConfiguration : IEntityTypeConfiguration<BackupSource>
{
    public void Configure(EntityTypeBuilder<BackupSource> builder)
    {
        builder.ToTable("sources", "backup");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.SourceKind)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.TargetKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(s => s.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // One schedule per pairing. SetNull rather than Cascade: deleting the schedule out of band (from
        // /admin/schedules) must degrade the pairing to manual-only, never delete the backup itself.
        builder.HasIndex(s => s.ScheduleId).IsUnique();

        builder.HasOne<ProcessingSchedule>()
            .WithMany()
            .HasForeignKey(s => s.ScheduleId)
            .OnDelete(DeleteBehavior.SetNull);

        // NO fluent unique index on (SourceKind, TargetKey) — deliberately, and do not add one.
        //
        // The invariant is "one pairing per kind per target" where target keys compare the way config
        // keys do: case-insensitively. A fluent HasIndex would emit a plain case-SENSITIVE index, which
        // would admit ('documents', 'Local') alongside ('documents', 'local') — two rows sharing one
        // RootFolder, each pruning the other's files by anti-join (see BackupSource's remarks).
        //
        // The correct index is an expression index on lower("TargetKey"), which the fluent API cannot
        // express, so it is created by raw SQL in the GeneralizeBackupSources migration and is therefore
        // INVISIBLE to the model snapshot. Nothing here will manage or recreate it; this comment is the
        // only guard.
    }
}
