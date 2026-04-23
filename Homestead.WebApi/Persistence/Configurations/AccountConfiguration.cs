using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", "finances");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.AccountType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(a => a.Institution)
            .HasMaxLength(128);

        builder.Property(a => a.IsMultiPosition).IsRequired();
        builder.Property(a => a.IsSystem).IsRequired();
        builder.Property(a => a.IsArchived).IsRequired();

        builder.Property(a => a.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(a => a.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(a => a.Parent)
            .WithMany(a => a.Children)
            .HasForeignKey(a => a.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.DefaultCommodity)
            .WithMany()
            .HasForeignKey(a => a.DefaultCommodityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.CreatedByUser)
            .WithMany()
            .HasForeignKey(a => a.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.UpdatedByUser)
            .WithMany()
            .HasForeignKey(a => a.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => a.ParentId);

        builder.HasIndex(a => a.AccountType)
            .HasFilter("\"IsArchived\" = FALSE");
    }
}
