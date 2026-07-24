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

        // NO foreign key to document_versions or documents. These rows are tombstones: they must survive
        // the permanent deletion of the version they describe, because at that moment the row is the only
        // thing that knows a file needs removing from the mirror. See DocumentBackupState's remarks.
        //
        // BackupSourceId is the exception and DOES cascade — deleting a pairing is an explicit "forget
        // this backup", so its bookkeeping goes with it.
        builder.HasOne<BackupSource>()
            .WithMany()
            .HasForeignKey(s => s.BackupSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // One copy per (source, version) — the invariant the whole diff rests on. Also serves the
        // planner's own load (every row for one source) as a leading-column prefix.
        builder.HasIndex(s => new { s.BackupSourceId, s.DocumentVersionId }).IsUnique();

        // Per-document lookups (all versions of one document in one pairing).
        builder.HasIndex(s => new { s.BackupSourceId, s.DocumentId });
    }
}
