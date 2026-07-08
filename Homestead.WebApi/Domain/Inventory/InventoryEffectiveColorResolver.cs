namespace Homestead.WebApi.Domain.Inventory;

public static class InventoryEffectiveColorResolver
{
    public static void Recompute(Tag tag, string? parentEffectiveColor, IReadOnlyList<Tag> descendants)
    {
        tag.EffectiveColor = tag.Color ?? parentEffectiveColor;

        var effectiveColors = new Dictionary<Guid, string?> { [tag.Id] = tag.EffectiveColor };

        foreach (var d in descendants.OrderBy(d => d.Path.Length))
        {
            var parentEc = d.ParentId.HasValue && effectiveColors.TryGetValue(d.ParentId.Value, out var ec)
                ? ec
                : tag.EffectiveColor;
            d.EffectiveColor = d.Color ?? parentEc;
            effectiveColors[d.Id] = d.EffectiveColor;
        }
    }
}
