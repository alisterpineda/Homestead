using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// Whole-set replacement of an item's tag join rows (mirrors
/// <c>DocumentMetadataService.SetTags</c>). Used by all four create/patch handlers.
/// </summary>
public static class InventoryItemTagSync
{
    public static void SetItemTags(Item item, List<Tag> tags)
    {
        item.ItemTags.Clear();

        var existingTagIds = new HashSet<Guid>(item.ItemTags.Select(it => it.TagId));
        foreach (var tag in tags)
        {
            if (existingTagIds.Add(tag.Id))
            {
                item.ItemTags.Add(new ItemTag
                {
                    ItemId = item.Id,
                    TagId = tag.Id,
                    Tag = tag
                });
            }
        }
    }
}
