using System.Linq.Expressions;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies;

internal static class SupplyResponseMapper
{
    /// <summary>
    /// EF projection that sums the per-location stock entries into a total on-hand and derives the
    /// low-stock flag in the database. Reused by the list/detail queries and the post-save reloads.
    /// </summary>
    public static readonly Expression<Func<Item, SupplyResponse>> Projection = i => new SupplyResponse
    {
        Id = i.Id,
        Name = i.Name,
        Status = i.Status,
        Notes = i.Notes,
        Unit = i.Unit!,
        RestockThreshold = i.RestockThreshold,
        TotalOnHand = i.StockEntries.Sum(e => e.Quantity),
        IsLow = i.RestockThreshold != null && i.StockEntries.Sum(e => e.Quantity) <= i.RestockThreshold,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
    };
}
