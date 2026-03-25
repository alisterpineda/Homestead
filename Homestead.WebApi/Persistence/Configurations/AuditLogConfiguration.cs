using Homestead.WebApi.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", "auditing");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityType)
            .IsRequired();

        builder.Property(a => a.Action)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Changes)
            .HasColumnType("jsonb");

        builder.Property(a => a.Timestamp)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.ParentEntityId);
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.UserId);
    }
}
