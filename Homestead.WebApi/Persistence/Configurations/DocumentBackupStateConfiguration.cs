using Homestead.WebApi.Domain.Backup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class DocumentBackupStateConfiguration : IEntityTypeConfiguration<DocumentBackupState>
{
    public void Configure(EntityTypeBuilder<DocumentBackupState> builder)
    {
        builder.ToTable("document_backup_state", "backup");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.TargetKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(s => s.RemoteRelativePath)
            .IsRequired();

        builder.Property(s => s.BackedUpHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.FileSize)
            .IsRequired();

        builder.Property(s => s.BackedUpAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // NO foreign key to document_versions or documents, and none to target_state either. These rows
        // are tombstones: they must survive the permanent deletion of the version they describe, because
        // at that moment the row is the only thing that knows a file needs removing from the mirror. See
        // DocumentBackupState's remarks.

        // One copy per (target, version) — the invariant the whole diff rests on.
        builder.HasIndex(s => new { s.TargetKey, s.DocumentVersionId }).IsUnique();

        // Per-document lookups (all versions of one document in one target).
        builder.HasIndex(s => new { s.TargetKey, s.DocumentId });

        // The planner's own load: every row for one target.
        builder.HasIndex(s => s.TargetKey);
    }
}
