using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class PayeeMappingConfiguration : IEntityTypeConfiguration<PayeeMapping>
{
    public void Configure(EntityTypeBuilder<PayeeMapping> builder)
    {
        builder.ToTable("payee_mappings", "finances");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.NormalizedKey)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(m => m.NormalizationVersion)
            .IsRequired();

        builder.Property(m => m.SampleRawDescription)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(m => m.TimesConfirmed)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(m => m.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(m => m.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // The mapping keys on a materialized column (not lower("Name") like PayeeConfiguration), so the
        // uniqueness index is a plain unique index EF can scaffold — no raw SQL needed. This is what the
        // ON CONFLICT ("NormalizedKey") DO UPDATE upsert in PayeeMappingService targets.
        builder.HasIndex(m => m.NormalizedKey)
            .IsUnique();

        // A mapping must never pin its payee alive — deleting the payee cascades its mappings away.
        builder.HasOne(m => m.Payee)
            .WithMany()
            .HasForeignKey(m => m.PayeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.CreatedByUser)
            .WithMany()
            .HasForeignKey(m => m.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.UpdatedByUser)
            .WithMany()
            .HasForeignKey(m => m.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
