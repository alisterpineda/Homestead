using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumableMovements;

/// <summary>Pages a consumable's movement ledger, newest-first. Returns null when the consumable is missing.</summary>
public class GetConsumableMovementsHandler(AppDbContext db, IItemLocationResolver locationResolver)
{
    public async Task<PaginatedResponse<StockMovementResponse>?> HandleAsync(
        Guid id, PaginatedRequest paging, CancellationToken ct)
    {
        var exists = await db.Items.AsNoTracking()
            .AnyAsync(i => i.Id == id && i.Fungibility == Fungibility.Fungible, ct);
        if (!exists)
            return null;

        var page = await db.StockMovements.AsNoTracking()
            .Where(m => m.ItemId == id)
            .OrderByDescending(m => m.OccurredAt)
            .ThenByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new MovementRow
            {
                Id = m.Id,
                Kind = m.Kind,
                Quantity = m.Quantity,
                FromAreaId = m.FromAreaId,
                FromContainerItemId = m.FromContainerItemId,
                ToAreaId = m.ToAreaId,
                ToContainerItemId = m.ToContainerItemId,
                UnitCost = m.UnitCost,
                UnitCostCurrencyId = m.UnitCostCurrencyId,
                UnitCostCurrencySymbol = m.UnitCostCurrency != null ? m.UnitCostCurrency.Symbol : null,
                OccurredAt = m.OccurredAt,
                RelatedItemId = m.RelatedItemId,
                RelatedItemName = m.RelatedItem != null ? m.RelatedItem.Name : null,
                CreatedByUserId = m.CreatedByUserId,
                CreatedByDisplayName = m.CreatedByUser != null ? m.CreatedByUser.DisplayName : null,
                Note = m.Note,
                Source = m.Source,
                CreatedAt = m.CreatedAt,
            })
            .ToPaginatedAsync(paging, ct);

        var locations = page.Items
            .SelectMany(m => new[]
            {
                new HoldingLocation(m.FromAreaId, m.FromContainerItemId),
                new HoldingLocation(m.ToAreaId, m.ToContainerItemId),
            })
            .ToList();
        var paths = await locationResolver.ResolveHoldingLocationsAsync(locations, ct);

        string? Label(Guid? areaId, Guid? containerId) =>
            paths.GetValueOrDefault(new HoldingLocation(areaId, containerId), LocationPath.Empty).Label;

        var items = page.Items.Select(m => new StockMovementResponse
        {
            Id = m.Id,
            Kind = m.Kind,
            Quantity = m.Quantity,
            FromAreaId = m.FromAreaId,
            FromContainerItemId = m.FromContainerItemId,
            FromLabel = Label(m.FromAreaId, m.FromContainerItemId),
            ToAreaId = m.ToAreaId,
            ToContainerItemId = m.ToContainerItemId,
            ToLabel = Label(m.ToAreaId, m.ToContainerItemId),
            UnitCost = m.UnitCost,
            UnitCostCurrencyId = m.UnitCostCurrencyId,
            UnitCostCurrencySymbol = m.UnitCostCurrencySymbol,
            OccurredAt = m.OccurredAt,
            RelatedItemId = m.RelatedItemId,
            RelatedItemName = m.RelatedItemName,
            CreatedByUserId = m.CreatedByUserId,
            CreatedByDisplayName = m.CreatedByDisplayName,
            Note = m.Note,
            Source = m.Source,
            CreatedAt = m.CreatedAt,
        }).ToList();

        return new PaginatedResponse<StockMovementResponse>
        {
            Items = items,
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
        };
    }

    private record MovementRow
    {
        public required Guid Id { get; init; }
        public required MovementKind Kind { get; init; }
        public required decimal Quantity { get; init; }
        public Guid? FromAreaId { get; init; }
        public Guid? FromContainerItemId { get; init; }
        public Guid? ToAreaId { get; init; }
        public Guid? ToContainerItemId { get; init; }
        public decimal? UnitCost { get; init; }
        public Guid? UnitCostCurrencyId { get; init; }
        public string? UnitCostCurrencySymbol { get; init; }
        public required DateTimeOffset OccurredAt { get; init; }
        public Guid? RelatedItemId { get; init; }
        public string? RelatedItemName { get; init; }
        public Guid? CreatedByUserId { get; init; }
        public string? CreatedByDisplayName { get; init; }
        public string? Note { get; init; }
        public string? Source { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
