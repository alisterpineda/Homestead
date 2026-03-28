using Homestead.WebApi.Domain.Groups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups", "auth");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(g => g.Name)
            .IsUnique();

        builder.Property(g => g.Description)
            .HasMaxLength(1024);

        builder.Property(g => g.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(g => g.UpdatedAt);

        builder.HasOne(g => g.UpdatedByUser)
            .WithMany()
            .HasForeignKey(g => g.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(g => g.CreatedByUser)
            .WithMany()
            .HasForeignKey(g => g.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
