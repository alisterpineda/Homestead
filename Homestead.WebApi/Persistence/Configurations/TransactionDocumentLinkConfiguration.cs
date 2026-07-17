using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class TransactionDocumentLinkConfiguration : IEntityTypeConfiguration<TransactionDocumentLink>
{
    public void Configure(EntityTypeBuilder<TransactionDocumentLink> builder)
    {
        builder.ToTable("transaction_document_links", "finances");

        builder.HasKey(l => new { l.AccountTransactionId, l.DocumentId });

        builder.Property(l => l.Origin)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(l => l.CreatedAt).IsRequired();

        builder.HasOne(l => l.AccountTransaction)
            .WithMany()
            .HasForeignKey(l => l.AccountTransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK without a navigation: link rows cascade away with the document (no purge-time cleanup
        // hook needed), but no C# navigation exists because Document's global DeletedAt filter would
        // silently drop trashed documents from any nav join (see comment on the entity).
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(l => l.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => l.DocumentId);
    }
}
