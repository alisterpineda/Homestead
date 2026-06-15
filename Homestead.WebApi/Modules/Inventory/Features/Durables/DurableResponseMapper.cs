using System.Linq.Expressions;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables;

internal static class DurableResponseMapper
{
    /// <summary>
    /// EF projection that resolves the read-time joins (<c>AreaName</c>, <c>ValueCurrencySymbol</c>)
    /// via LEFT JOINs. Reused by the list/detail queries and the post-save reload in create/patch.
    /// </summary>
    public static readonly Expression<Func<Item, DurableResponse>> Projection = i => new DurableResponse
    {
        Id = i.Id,
        Name = i.Name,
        Fungibility = i.Fungibility,
        Status = i.Status,
        Notes = i.Notes,
        AreaId = i.AreaId,
        AreaName = i.Area != null ? i.Area.Name : null,
        ParentItemId = i.ParentItemId,
        IsContainer = i.IsContainer,
        Compartment = i.Compartment,
        Value = i.Value,
        ValueCurrencyId = i.ValueCurrencyId,
        ValueCurrencySymbol = i.ValueCurrency != null ? i.ValueCurrency.Symbol : null,
        Serial = i.Serial,
        AcquiredOn = i.AcquiredOn,
        LentTo = i.LentTo,
        Unit = i.Unit,
        RestockThreshold = i.RestockThreshold,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
    };
}
