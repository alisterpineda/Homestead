using Homestead.WebApi.Domain.Backup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class BackupTargetFolderConfiguration : IEntityTypeConfiguration<BackupTargetFolder>
{
    public void Configure(EntityTypeBuilder<BackupTargetFolder> builder)
    {
        builder.ToTable("target_folders", "backup");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.TargetKey)
            .IsRequired()
            .HasMaxLength(128);

        // No length cap: the path is the planner's, whose segment/total limits are the destination's
        // (Drive allows 255-byte names and very deep trees), and truncating one would silently point two
        // different folders at one id.
        builder.Property(f => f.Path)
            .IsRequired();

        builder.Property(f => f.RemoteFolderId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(f => f.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // The map's whole point: one id per path per target, enforced by the database rather than by the
        // ensure step's own care. Two rows for one path would let two batches write into twin folders.
        builder.HasIndex(f => new { f.TargetKey, f.Path }).IsUnique();

        // Cascade from the target-state row: disconnecting (or re-connecting a different account) makes
        // every stored id meaningless, so the map must not outlive the grant it was built under.
        builder.HasOne<BackupTargetState>()
            .WithMany()
            .HasForeignKey(f => f.TargetKey)
            .HasPrincipalKey(s => s.TargetKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
