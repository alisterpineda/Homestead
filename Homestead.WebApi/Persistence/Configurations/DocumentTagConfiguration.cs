using Homestead.WebApi.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class DocumentTagConfiguration : IEntityTypeConfiguration<DocumentTag>
{
    public void Configure(EntityTypeBuilder<DocumentTag> builder)
    {
        builder.ToTable("document_tags", "documents");

        builder.HasKey(dt => new { dt.DocumentId, dt.TagId });

        builder.HasIndex(dt => dt.DocumentId);
        builder.HasIndex(dt => dt.TagId);

        builder.HasOne(dt => dt.Document)
            .WithMany(d => d.DocumentTags)
            .HasForeignKey(dt => dt.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dt => dt.Tag)
            .WithMany(t => t.DocumentTags)
            .HasForeignKey(dt => dt.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
