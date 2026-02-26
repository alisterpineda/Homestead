using HomeSuiteHome.WebApi.Domains.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeSuiteHome.WebApi.Persistence.Configurations;

public class DocumentRevisionConfiguration : IEntityTypeConfiguration<DocumentRevision>
{
    public void Configure(EntityTypeBuilder<DocumentRevision> builder)
    {
        builder.ToTable("document_revisions", "documents");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.RevisionNumber)
            .IsRequired();

        builder.Property(r => r.Label)
            .HasMaxLength(256);

        builder.Property(r => r.Notes)
            .HasMaxLength(2048);

        builder.Property(r => r.ContentType)
            .IsRequired();

        builder.Property(r => r.FileSize)
            .IsRequired();

        builder.Property(r => r.FilePath)
            .IsRequired();

        builder.Property(r => r.FileHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.FileExtension)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(r => r.ExtractedText)
            .HasColumnType("text");

        builder.Property(r => r.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(r => r.Document)
            .WithMany(d => d.Revisions)
            .HasForeignKey(r => r.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.DocumentId);
        builder.HasIndex(r => r.FileHash).IsUnique();
        builder.HasIndex(r => new { r.DocumentId, r.RevisionNumber }).IsUnique();
    }
}
