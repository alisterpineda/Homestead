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
    }
}
