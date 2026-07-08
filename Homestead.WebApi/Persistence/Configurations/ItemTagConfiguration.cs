using Homestead.WebApi.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class ItemTagConfiguration : IEntityTypeConfiguration<ItemTag>
{
    public void Configure(EntityTypeBuilder<ItemTag> builder)
    {
        builder.ToTable("item_tags", "inventory");

        builder.HasKey(it => new { it.ItemId, it.TagId });

        builder.HasIndex(it => it.ItemId);
        builder.HasIndex(it => it.TagId);

        builder.HasOne(it => it.Item)
            .WithMany(i => i.ItemTags)
            .HasForeignKey(it => it.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(it => it.Tag)
            .WithMany(t => t.ItemTags)
            .HasForeignKey(it => it.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
