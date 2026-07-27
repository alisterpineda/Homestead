using Homestead.WebApi.Domain.Backup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class BackupUploadSessionConfiguration : IEntityTypeConfiguration<BackupUploadSession>
{
    public void Configure(EntityTypeBuilder<BackupUploadSession> builder)
    {
        builder.ToTable("upload_sessions", "backup");

        builder.HasKey(s => s.Id);

        // Session URIs are long, opaque, and issued by the destination — no useful cap to impose.
        builder.Property(s => s.SessionUri)
            .IsRequired();

        builder.Property(s => s.FileHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.BytesCommitted)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // One in-flight upload per (pairing, version) — a second would race the first's offset.
        builder.HasIndex(s => new { s.BackupSourceId, s.DocumentVersionId }).IsUnique();

        // Cascades with the pairing, exactly as its state rows do: deleting the pairing is "forget this
        // backup", and a parked session is bookkeeping about work that will never be resumed.
        builder.HasOne<BackupSource>()
            .WithMany()
            .HasForeignKey(s => s.BackupSourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
