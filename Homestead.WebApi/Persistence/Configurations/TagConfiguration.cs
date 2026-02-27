using Homestead.WebApi.Domains.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags", "documents");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.Path)
            .IsRequired()
            .HasMaxLength(1024);

        builder.HasIndex(t => t.Path)
            .IsUnique();

        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(t => t.Parent)
            .WithMany(t => t.Children)
            .HasForeignKey(t => t.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
