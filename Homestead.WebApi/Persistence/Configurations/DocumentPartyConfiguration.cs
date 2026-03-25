using Homestead.WebApi.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class DocumentPartyConfiguration : IEntityTypeConfiguration<DocumentParty>
{
    public void Configure(EntityTypeBuilder<DocumentParty> builder)
    {
        builder.ToTable("document_parties", "documents");

        builder.HasKey(dp => new { dp.DocumentId, dp.PartyId });

        builder.HasIndex(dp => dp.DocumentId);
        builder.HasIndex(dp => dp.PartyId);

        builder.HasOne(dp => dp.Document)
            .WithMany(d => d.DocumentParties)
            .HasForeignKey(dp => dp.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dp => dp.Party)
            .WithMany(p => p.DocumentParties)
            .HasForeignKey(dp => dp.PartyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
