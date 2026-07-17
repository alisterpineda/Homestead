using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class NormalTransactionConfiguration : IEntityTypeConfiguration<NormalTransaction>
{
    public void Configure(EntityTypeBuilder<NormalTransaction> builder)
    {
        // Second discriminator on the shared TPH table. String-converted like Type so the column
        // is human-readable; nullable in the table automatically because it lives on a derived type.
        builder.Property(t => t.NormalShape)
            .HasConversion<string>()
            .HasMaxLength(32);

        // Optional counterparty for flows. Restrict delete: an in-use payee cannot be removed
        // (the delete slice guards this with a friendlier 409 + archive suggestion).
        builder.HasOne<Payee>()
            .WithMany()
            .HasForeignKey(t => t.PayeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.PayeeId);
    }
}
