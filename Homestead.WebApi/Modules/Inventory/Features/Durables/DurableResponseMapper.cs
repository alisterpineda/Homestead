using System.Linq.Expressions;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables;

internal static class DurableResponseMapper
{
    /// <summary>
    /// EF projection that resolves the read-time joins (<c>AreaName</c>, <c>ValueCurrencySymbol</c>)
    /// via LEFT JOINs. Reused by the list/detail queries and the post-save reload in create/patch.
    /// Resolves <c>WarrantyStatus</c> against the server's UTC date; use <see cref="For"/> to pin an
    /// explicit date.
    /// </summary>
    public static Expression<Func<Item, DurableResponse>> Projection =>
        For(DateOnly.FromDateTime(DateTime.UtcNow));

    /// <summary>
    /// The projection with an explicit "today", so callers that already resolved a date through
    /// <c>TimeProvider</c> stay consistent with it.
    /// </summary>
    public static Expression<Func<Item, DurableResponse>> For(DateOnly today)
    {
        // Captured as closure locals so EF parameterizes them: the expression tree's shape stays
        // identical across calls and the compiled-query cache keeps hitting.
        var cutoff = WarrantyStatusRules.Cutoff(today);

        return i => new DurableResponse
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
                c.Status == ItemStatus.Active || c.Status == ItemStatus.Lent),
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
            StatusNote = i.StatusNote,
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
            // The item-level rule, in SQL: an open-ended row wins outright, otherwise the latest expiry
            // decides. GetInventoryOverviewHandler's Expiring predicate is its twin — the two must stay
            // in sync (same posture as the duplicated low-stock predicate), with row-level edges
            // matching WarrantyStatusRules.ForRow. Ordered so the open-ended and no-rows cases
            // are settled before Max() is asked for anything: SQL's MAX ignores NULLs, so the comparisons
            // below only ever see a set of real dates.
            WarrantyStatus =
                i.Warranties.Any(w => w.ExpiresOn == null) ? WarrantyStatus.Active
                : !i.Warranties.Any() ? WarrantyStatus.None
                : i.Warranties.Max(w => w.ExpiresOn) < today ? WarrantyStatus.Expired
                : i.Warranties.Max(w => w.ExpiresOn) <= cutoff ? WarrantyStatus.Expiring
                : WarrantyStatus.Active,
        };
    }
}
