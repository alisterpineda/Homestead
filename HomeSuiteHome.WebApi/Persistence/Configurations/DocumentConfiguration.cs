using HomeSuiteHome.WebApi.Domains.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeSuiteHome.WebApi.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", "documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name)
            .IsRequired();

        builder.Property(d => d.ContentType)
            .IsRequired();

        builder.Property(d => d.FileSize)
            .IsRequired();

        builder.Property(d => d.FilePath)
            .IsRequired();

        builder.Property(d => d.FileHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(d => d.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(d => d.ExtractedText)
            .HasColumnType("text");

        builder.HasOne(d => d.CreatedByUser)
            .WithMany()
            .HasForeignKey(d => d.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.OwnerUser)
            .WithMany()
            .HasForeignKey(d => d.OwnerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => d.CreatedByUserId);
        builder.HasIndex(d => d.OwnerUserId);
    }
}