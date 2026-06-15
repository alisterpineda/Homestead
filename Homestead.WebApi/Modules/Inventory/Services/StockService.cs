using System.Globalization;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// The one place that knows how to keep stock entries and the movement ledger in agreement. See
/// <see cref="IStockService"/>. Decrements are applied as atomic, guarded database-level updates so
/// concurrent uses both subtract rather than overwriting a stale read; the <c>Quantity &gt;= 0</c>
/// check constraint is the last-resort backstop. Has no unit coverage by construction (the funnel
/// relies on real-database semantics InMemory can't honour) — the integration suite is the net.
/// </summary>
public class StockService(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit) : IStockService
{
    // Matches the StockMovement.Note column length; reject over-long notes with a clean 400 rather
    // than letting them reach the INSERT and surface as a raw database-length 500.
    private const int NoteMaxLength = 1000;

    public async Task ReceiveAsync(Guid itemId, StockReceiveRequest request, CancellationToken ct)
    {
        var item = await LoadFungibleItemAsync(itemId, ct);
        RequirePositive(request.Quantity);
        await ValidateLocationAsync(request.To, ct);
        await ValidateUnitCostAsync(request.UnitCost, request.UnitCostCurrencyId, ct);
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        await ApplyDeltaAsync(itemId, request.To, request.Quantity, occurredAt, ct);

        AppendMovement(item, MovementKind.Flow, from: null, to: request.To, request.Quantity,
            request.UnitCost, request.UnitCostCurrencyId, occurredAt, relatedItemId: null, request.Note);
    }

    public async Task ConsumeAsync(Guid itemId, StockConsumeRequest request, CancellationToken ct)
    {
        var item = await LoadFungibleItemAsync(itemId, ct);
        RequirePositive(request.Quantity);
        await ValidateLocationAsync(request.From, ct);
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        await ApplyDeltaAsync(itemId, request.From, -request.Quantity, occurredAt, ct);

        AppendMovement(item, MovementKind.Flow, from: request.From, to: null, request.Quantity,
            unitCost: null, unitCostCurrencyId: null, occurredAt, relatedItemId: null, request.Note);
    }

    public async Task TransferAsync(Guid itemId, StockTransferRequest request, CancellationToken ct)
    {
        var item = await LoadFungibleItemAsync(itemId, ct);
        RequirePositive(request.Quantity);
        await ValidateLocationAsync(request.From, ct);
        await ValidateLocationAsync(request.To, ct);
        if (SameLocation(request.From, request.To))
            throw new BusinessRuleException("Transfer source and destination must differ.");
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        // Decrement the source first so an over-transfer fails before any increment is applied.
        await ApplyDeltaAsync(itemId, request.From, -request.Quantity, occurredAt, ct);
        await ApplyDeltaAsync(itemId, request.To, request.Quantity, occurredAt, ct);

        AppendMovement(item, MovementKind.Flow, from: request.From, to: request.To, request.Quantity,
            unitCost: null, unitCostCurrencyId: null, occurredAt, relatedItemId: null, request.Note);
    }

    public async Task AdjustAsync(Guid itemId, StockAdjustRequest request, CancellationToken ct)
    {
        var item = await LoadFungibleItemAsync(itemId, ct);
        if (request.NewQuantity < 0)
            throw new BusinessRuleException("Quantity must be zero or positive.");
        await ValidateLocationAsync(request.Location, ct);
        var occurredAt = ResolveOccurredAt(request.OccurredAt);
        var now = DateTimeOffset.UtcNow;

        var entry = await FindEntryAsync(itemId, request.Location, ct);
        var current = entry?.Quantity ?? 0m;
        var delta = request.NewQuantity - current;
        if (delta == 0m)
            throw new BusinessRuleException("The recorded quantity already matches — nothing to correct.");

        // An adjustment is an authoritative recount: the new value is set absolutely, not as a delta.
        if (entry is null)
        {
            var created = NewEntry(itemId, request.Location, request.NewQuantity, now);
            db.StockEntries.Add(created);
            audit.Created("StockEntry", created.Id);
        }
        else if (request.NewQuantity == 0m)
        {
            await db.StockEntries.Where(e => e.Id == entry.Id).ExecuteDeleteAsync(ct);
            audit.Deleted("StockEntry", entry.Id, item.Name);
        }
        else
        {
            await db.StockEntries.Where(e => e.Id == entry.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Quantity, request.NewQuantity)
                    .SetProperty(e => e.UpdatedAt, now), ct);
            audit.TrackScalar("StockEntry", entry.Id, "Quantity",
                Format(current), Format(request.NewQuantity));
        }

        AppendMovement(item, MovementKind.Adjustment,
            from: delta < 0 ? request.Location : null,
            to: delta > 0 ? request.Location : null,
            Math.Abs(delta), unitCost: null, unitCostCurrencyId: null, occurredAt,
            relatedItemId: null, request.Note);
    }

    public async Task ConvertOutAsync(Guid itemId, StockConvertOutRequest request, CancellationToken ct)
    {
        var item = await LoadConvertibleItemAsync(itemId, ct);
        await ValidateLocationAsync(request.From, ct);
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        // Whole-number proxy for "count-like": track-one only makes sense when the source holds a
        // whole number of discrete units. This is an advisory pre-check — the guarded decrement below
        // is what actually prevents an over-draw under a concurrent fractional consume.
        var entry = await FindEntryAsync(itemId, request.From, ct);
        if (entry is null)
            throw new BusinessRuleException("There is no stock of this item at the source location.");
        if (entry.Quantity < 1m || entry.Quantity % 1m != 0m)
            throw new BusinessRuleException(
                "Track one individually requires a whole-number quantity of at least 1 at this location.");

        await ApplyDeltaAsync(itemId, request.From, -1m, occurredAt, ct);

        AppendMovement(item, MovementKind.Conversion, from: request.From, to: null, 1m,
            unitCost: null, unitCostCurrencyId: null, occurredAt,
            relatedItemId: request.RelatedItemId, request.Note);
    }

    public async Task ConvertInAsync(Guid itemId, StockConvertInRequest request, CancellationToken ct)
    {
        var item = await LoadConvertibleItemAsync(itemId, ct);
        await ValidateLocationAsync(request.To, ct);
        if (request.RelatedItemIds.Count == 0)
            throw new BusinessRuleException("At least one item must be selected to collapse.");
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        await ApplyDeltaAsync(itemId, request.To, request.RelatedItemIds.Count, occurredAt, ct);

        // One quantity-1 inflow per collapsed instance, each carrying its id — full two-way
        // traceability, symmetric with track-one.
        foreach (var relatedItemId in request.RelatedItemIds)
        {
            AppendMovement(item, MovementKind.Conversion, from: null, to: request.To, 1m,
                unitCost: null, unitCostCurrencyId: null, occurredAt, relatedItemId, request.Note);
        }
    }

    /// <summary>
    /// Applies a signed delta to the entry at a location, creating/merging on increment and
    /// deleting on reaching zero. A decrement is a guarded atomic update; an over-draw is rejected.
    /// </summary>
    private async Task ApplyDeltaAsync(
        Guid itemId, StockLocation location, decimal delta, DateTimeOffset now, CancellationToken ct)
    {
        if (delta == 0m)
            return;

        var entry = await FindEntryAsync(itemId, location, ct);

        if (delta > 0m)
        {
            if (entry is null)
            {
                var created = NewEntry(itemId, location, delta, now);
                db.StockEntries.Add(created);
                audit.Created("StockEntry", created.Id);
            }
            else
            {
                await db.StockEntries.Where(e => e.Id == entry.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.Quantity, e => e.Quantity + delta)
                        .SetProperty(e => e.UpdatedAt, now), ct);
                audit.TrackScalar("StockEntry", entry.Id, "Quantity",
                    Format(entry.Quantity), Format(entry.Quantity + delta));
            }

            return;
        }

        var amount = -delta;
        if (entry is null)
            throw new BusinessRuleException("There is no stock of this item at the source location.");
        if (entry.Quantity < amount)
            throw new BusinessRuleException(
                $"Only {Format(entry.Quantity)} on hand at the source location — cannot remove {Format(amount)}.");

        // Guarded decrement: race-safe against a concurrent use and never drives the row negative.
        var affected = await db.StockEntries
            .Where(e => e.Id == entry.Id && e.Quantity >= amount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Quantity, e => e.Quantity - amount)
                .SetProperty(e => e.UpdatedAt, now), ct);
        if (affected == 0)
            throw new BusinessRuleException("Not enough on hand at the source location.");

        // A zero-quantity entry is removed, not kept; the history survives in the ledger. The purge
        // decision is made from what the row ACTUALLY holds after the guarded update (WHERE Quantity
        // == 0), not from the pre-update snapshot — so a concurrent decrement that drove it to zero
        // can't leave an orphan zero-quantity row behind.
        var purged = await db.StockEntries
            .Where(e => e.Id == entry.Id && e.Quantity == 0m)
            .ExecuteDeleteAsync(ct);
        if (purged > 0)
            audit.Deleted("StockEntry", entry.Id);
        else
            audit.TrackScalar("StockEntry", entry.Id, "Quantity",
                Format(entry.Quantity), Format(entry.Quantity - amount));
    }

    private void AppendMovement(
        Item item, MovementKind kind, StockLocation? from, StockLocation? to, decimal quantity,
        decimal? unitCost, Guid? unitCostCurrencyId, DateTimeOffset occurredAt, Guid? relatedItemId,
        string? note)
    {
        var trimmedNote = Normalize(note);
        if (trimmedNote is { Length: > NoteMaxLength })
            throw new BusinessRuleException($"Note must be {NoteMaxLength} characters or fewer.");

        db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            ItemId = item.Id,
            Kind = kind,
            FromAreaId = from?.AreaId,
            FromContainerItemId = from?.ContainerItemId,
            ToAreaId = to?.AreaId,
            ToContainerItemId = to?.ContainerItemId,
            Quantity = quantity,
            UnitCost = unitCost,
            UnitCostCurrencyId = unitCostCurrencyId,
            OccurredAt = occurredAt,
            CreatedByUserId = currentUser.UserId,
            RelatedItemId = relatedItemId,
            Note = trimmedNote,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private Task<StockEntry?> FindEntryAsync(Guid itemId, StockLocation location, CancellationToken ct) =>
        db.StockEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ItemId == itemId
                && e.AreaId == location.AreaId
                && e.ContainerItemId == location.ContainerItemId, ct);

    private static StockEntry NewEntry(Guid itemId, StockLocation location, decimal quantity, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            ItemId = itemId,
            AreaId = location.AreaId,
            ContainerItemId = location.ContainerItemId,
            Quantity = quantity,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private async Task<Item> LoadFungibleItemAsync(Guid itemId, CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");
        if (item.Fungibility != Fungibility.Fungible)
            throw new BusinessRuleException("Stock operations apply only to fungible consumables.");
        return item;
    }

    // Conversions additionally require the consumable to be Active — unlike receive/consume/transfer/
    // adjust, converting an Archived consumable has no meaning (you can't carve from or refill a retired
    // record). The other ops don't yet share this gate (see the plan's accepted tradeoff).
    private async Task<Item> LoadConvertibleItemAsync(Guid itemId, CancellationToken ct)
    {
        var item = await LoadFungibleItemAsync(itemId, ct);
        if (item.Status != ItemStatus.Active)
            throw new BusinessRuleException("Conversions require an Active consumable.");
        return item;
    }

    private async Task ValidateLocationAsync(StockLocation location, CancellationToken ct)
    {
        if (!location.IsValid)
            throw new BusinessRuleException("A stock location must be exactly one of an area or a container.");

        if (location.AreaId is { } areaId)
        {
            if (!await db.Areas.AnyAsync(a => a.Id == areaId, ct))
                throw new BusinessRuleException("AreaId must reference an existing area.");
            return;
        }

        var containerId = location.ContainerItemId!.Value;
        var container = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == containerId, ct);
        if (container is null)
            throw new BusinessRuleException("ContainerItemId must reference an existing item.");
        if (container.Fungibility != Fungibility.NonFungible || !container.IsContainer)
            throw new BusinessRuleException("ContainerItemId must reference a container item.");
    }

    private async Task ValidateUnitCostAsync(decimal? unitCost, Guid? currencyId, CancellationToken ct)
    {
        if (unitCost.HasValue != currencyId.HasValue)
            throw new BusinessRuleException("UnitCost and UnitCostCurrencyId must be set together.");
        if (unitCost is < 0m)
            throw new BusinessRuleException("UnitCost must be zero or positive.");
        if (currencyId is { } id)
        {
            var currency = await db.Commodities.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (currency is null)
                throw new BusinessRuleException("UnitCostCurrencyId must reference an existing commodity.");
            if (currency.Kind != CommodityKind.Currency)
                throw new BusinessRuleException("UnitCostCurrencyId must reference a currency commodity.");
        }
    }

    private static void RequirePositive(decimal quantity)
    {
        if (quantity <= 0m)
            throw new BusinessRuleException("Quantity must be greater than zero.");
    }

    private static bool SameLocation(StockLocation a, StockLocation b) =>
        a.AreaId == b.AreaId && a.ContainerItemId == b.ContainerItemId;

    private static DateTimeOffset ResolveOccurredAt(DateTimeOffset? occurredAt)
    {
        var resolved = occurredAt ?? DateTimeOffset.UtcNow;
        if (resolved > DateTimeOffset.UtcNow.AddMinutes(1))
            throw new BusinessRuleException("OccurredAt cannot be in the future.");
        return resolved;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
