using Homestead.WebApi.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class TextExtractionEngineConfiguration : IEntityTypeConfiguration<TextExtractionEngine>
{
    public void Configure(EntityTypeBuilder<TextExtractionEngine> builder)
    {
        builder.ToTable("text_extraction_engines", "settings");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(e => e.Name)
            .IsUnique();

        builder.Property(e => e.EngineType)
            .HasConversion<string>();

        builder.Property(e => e.Configuration)
            .HasColumnType("jsonb");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
