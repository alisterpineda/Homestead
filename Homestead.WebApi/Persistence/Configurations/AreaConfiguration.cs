using Homestead.WebApi.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        builder.ToTable("areas", "inventory");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(a => a.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Self-referencing parent (nested areas). Restrict mirrors the container chain
        // (ItemConfiguration) — a parent area can't be deleted while sub-areas reference it; the
        // handler raises a friendly message ahead of this FK backstop.
        builder.HasOne(a => a.ParentArea)
            .WithMany()
            .HasForeignKey(a => a.ParentAreaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.ParentAreaId);
    }
}
