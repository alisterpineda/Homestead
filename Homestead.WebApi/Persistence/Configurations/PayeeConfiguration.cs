using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class PayeeConfiguration : IEntityTypeConfiguration<Payee>
{
    public void Configure(EntityTypeBuilder<Payee> builder)
    {
        builder.ToTable("payees", "finances");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.Notes)
            .HasMaxLength(1024);

        builder.Property(p => p.IsArchived)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(p => p.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(p => p.DefaultCategory)
            .WithMany()
            .HasForeignKey(p => p.DefaultCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.CreatedByUser)
            .WithMany()
            .HasForeignKey(p => p.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.UpdatedByUser)
            .WithMany()
            .HasForeignKey(p => p.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // The canonicalising unique index on lower("Name") is a functional index EF can't scaffold;
        // it is added as raw SQL in the AddPayees migration (see BrandConfiguration for the idiom).
    }
}
