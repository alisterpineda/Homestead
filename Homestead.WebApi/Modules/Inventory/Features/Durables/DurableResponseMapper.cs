using System.Linq.Expressions;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;

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
        ParentIsContainer = i.ParentItem != null ? i.ParentItem.IsContainer : null,
        // "Holds something live": ≥1 live child, of either relationship. Read gated by IsContainer on
        // the client it yields assembly-ness (a non-container's live components); ungated it disables
        // the container flip (see DurableResponse.HasComponents). Non-terminal statuses are spelled
        // inline — IsTerminal()/LiveStatuses won't translate cleanly inside a projected .Any().
        HasComponents = i.Children.Any(c =>
            c.Status == ItemStatus.Active || c.Status == ItemStatus.Lent || c.Status == ItemStatus.InRepair),
        Compartment = i.Compartment,
        Value = i.Value,
        ValueCurrencyId = i.ValueCurrencyId,
        ValueCurrencySymbol = i.ValueCurrency != null ? i.ValueCurrency.Symbol : null,
        BrandId = i.BrandId,
        BrandName = i.Brand != null ? i.Brand.Name : null,
        Serial = i.Serial,
        Model = i.Model,
        AcquiredOn = i.AcquiredOn,
        AcquisitionKind = i.AcquisitionKind,
        AcquisitionCost = i.AcquisitionCost,
        AcquisitionCostCurrencyId = i.AcquisitionCostCurrencyId,
        AcquisitionCostCurrencySymbol = i.AcquisitionCostCurrency != null ? i.AcquisitionCostCurrency.Symbol : null,
        AcquiredFrom = i.AcquiredFrom,
        LentTo = i.LentTo,
        Unit = i.Unit,
        RestockThreshold = i.RestockThreshold,
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
