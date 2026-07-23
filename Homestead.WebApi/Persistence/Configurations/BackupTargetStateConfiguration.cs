using Homestead.WebApi.Domain.Backup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class BackupTargetStateConfiguration : IEntityTypeConfiguration<BackupTargetState>
{
    public void Configure(EntityTypeBuilder<BackupTargetState> builder)
    {
        builder.ToTable("target_state", "backup");

        // The config key IS the identity — a surrogate id would let two rows claim one target.
        builder.HasKey(s => s.TargetKey);

        builder.Property(s => s.TargetKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(s => s.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
