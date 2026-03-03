using Homestead.WebApi.Domains.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.ToTable("jobs", "processing");

        builder.HasKey(j => j.Id);

        builder.Property(j => j.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(j => j.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(j => j.EntityType)
            .HasConversion<string>();

        builder.Property(j => j.Payload)
            .HasColumnType("jsonb");

        builder.Property(j => j.Priority)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(j => j.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(j => j.Type);
        builder.HasIndex(j => j.EntityId);
        builder.HasIndex(j => new { j.Status, j.Priority });
        builder.HasIndex(j => j.CreatedByUserId);
    }
}
