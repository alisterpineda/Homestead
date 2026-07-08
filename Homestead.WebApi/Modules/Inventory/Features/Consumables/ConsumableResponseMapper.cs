using System.Linq.Expressions;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables;

internal static class ConsumableResponseMapper
{
    /// <summary>
    /// EF projection that sums the per-location stock entries into a total on-hand and derives the
    /// low-stock flag in the database. Reused by the list/detail queries and the post-save reloads.
    /// </summary>
    public static readonly Expression<Func<Item, ConsumableResponse>> Projection = i => new ConsumableResponse
    {
        Id = i.Id,
        Name = i.Name,
        Status = i.Status,
        Notes = i.Notes,
        Unit = i.Unit,
        RestockThreshold = i.RestockThreshold,
        BrandId = i.BrandId,
        BrandName = i.Brand != null ? i.Brand.Name : null,
        TotalOnHand = i.StockEntries.Sum(e => e.Quantity),
        IsLow = i.RestockThreshold != null && i.StockEntries.Sum(e => e.Quantity) <= i.RestockThreshold,
        Tags = i.ItemTags.Select(it => new ItemTagDto
        {
            Id = it.Tag.Id,
            Name = it.Tag.Name,
            Path = it.Tag.Path,
            Color = it.Tag.Color,
            EffectiveColor = it.Tag.EffectiveColor
        }).ToList(),
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
    };
}
