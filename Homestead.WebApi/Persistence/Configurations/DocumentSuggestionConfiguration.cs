using Homestead.WebApi.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class DocumentSuggestionConfiguration : IEntityTypeConfiguration<DocumentSuggestion>
{
    public void Configure(EntityTypeBuilder<DocumentSuggestion> builder)
    {
        builder.ToTable("document_suggestions", "documents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Field)
            .HasConversion<string>();

        builder.Property(e => e.Status)
            .HasConversion<string>();

        builder.Property(e => e.SuggestedValue)
            .IsRequired();

        builder.Property(e => e.IsApplied)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(e => e.Document)
            .WithMany(d => d.Suggestions)
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.DocumentId, e.Field });
    }
}
