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

    // Matches the StockMovement.Source column length; same clean-400 rationale as NoteMaxLength.
    private const int SourceMaxLength = 256;

    // Track-one's whole-number rule, rejected in two places (ConvertOutAsync's advisory pre-check and
    // ApplyDeltaAsync's race-path guard); kept as one string so the two can't drift apart.
    private const string WholeNumberRequiredMessage =
        "Track one individually requires a whole-number quantity of at least 1 at this location.";

    public async Task ReceiveAsync(Guid itemId, StockReceiveRequest request, CancellationToken ct)
    {
        var item = await LoadFungibleItemAsync(itemId, ct);
        RequirePositive(request.Quantity);
        await ValidateLocationAsync(request.To, ct, isDestination: true);
        await ValidateUnitCostAsync(request.UnitCost, request.UnitCostCurrencyId, ct);
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        await ApplyDeltaAsync(itemId, request.To, request.Quantity, ct);

        AppendMovement(item, MovementKind.Flow, from: null, to: request.To, request.Quantity,
            request.UnitCost, request.UnitCostCurrencyId, occurredAt, relatedItemId: null, request.Note,
            request.Source);
    }

    public async Task ConsumeAsync(Guid itemId, StockConsumeRequest request, CancellationToken ct)
    {
        var item = await LoadFungibleItemAsync(itemId, ct);
        RequirePositive(request.Quantity);
        await ValidateLocationAsync(request.From, ct);
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        await ApplyDeltaAsync(itemId, request.From, -request.Quantity, ct);

        AppendMovement(item, MovementKind.Flow, from: request.From, to: null, request.Quantity,
            unitCost: null, unitCostCurrencyId: null, occurredAt, relatedItemId: null, request.Note,
            source: null);
    }

    public async Task TransferAsync(Guid itemId, StockTransferRequest request, CancellationToken ct)
    {
        var item = await LoadFungibleItemAsync(itemId, ct);
        RequirePositive(request.Quantity);
        await ValidateLocationAsync(request.From, ct);
        await ValidateLocationAsync(request.To, ct, isDestination: true);
        if (SameLocation(request.From, request.To))
            throw new BusinessRuleException("Transfer source and destination must differ.");
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        // Decrement the source first so an over-transfer fails before any increment is applied.
        await ApplyDeltaAsync(itemId, request.From, -request.Quantity, ct);
        await ApplyDeltaAsync(itemId, request.To, request.Quantity, ct);

        AppendMovement(item, MovementKind.Flow, from: request.From, to: request.To, request.Quantity,
            unitCost: null, unitCostCurrencyId: null, occurredAt, relatedItemId: null, request.Note,
            source: null);
    }

    public async Task AdjustAsync(Guid itemId, StockAdjustRequest request, CancellationToken ct)
    {
        var item = await LoadFungibleItemAsync(itemId, ct);
        if (request.NewQuantity < 0)
            throw new BusinessRuleException("Quantity must be zero or positive.");
        await ValidateLocationAsync(request.Location, ct);
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        // Adjust is a source — you may recount stranded stock DOWN, out of a terminal container — but an
        // adjust that RAISES the count is an inflow, and nothing may be added INTO a terminal container
        // (B1). The container's status doesn't change during the command, so resolve it once here; the
        // per-attempt delta sign decides whether it applies (and the sign can flip across retries).
        var locationIsTerminalContainer = await IsTerminalContainerAsync(request.Location, ct);

        // An adjustment is an authoritative recount: the new value is set absolutely, not as a delta.
        // The write is guarded on the value we read and retried on a concurrent change, so the recorded
        // Adjustment magnitude always matches the change actually applied and a concurrent inflow can't be
        // silently discarded by an unconditional delete.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            // Record-time of THIS attempt's write, not a stale pre-loop instant (mirrors ApplyDeltaAsync).
            var now = DateTimeOffset.UtcNow;
            var entry = await FindEntryAsync(itemId, request.Location, ct);
            var current = entry?.Quantity ?? 0m;
            var delta = request.NewQuantity - current;

            // B9: a confirming recount changes nothing — a 200 no-op, no movement written (consistent
            // with the module's net-zero-writes-nothing stance).
            if (delta == 0m)
                return;

            // Adjusting UP inside a terminal container is an inflow — blocked like a receive/transfer-in.
            // Adjusting DOWN stays allowed (the reconcile-stranded-stock-out escape hatch).
            if (delta > 0m && locationIsTerminalContainer)
                throw new BusinessRuleException("Cannot add stock into an archived container.");

            if (entry is null)
            {
                AddNewEntry(itemId, request.Location, request.NewQuantity, now); // concurrent insert loses on unique index -> 409
                AppendAdjustmentMovement(item, request, delta, occurredAt);
                return;
            }

            // Guard the absolute write/delete on the value we read; retry on a concurrent change.
            var guard = db.StockEntries.Where(e => e.Id == entry.Id && e.Quantity == current);
            var isRemoval = request.NewQuantity == 0m;
            var affected = isRemoval
                ? await guard.ExecuteDeleteAsync(ct)
                : await guard.ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Quantity, request.NewQuantity)
                    .SetProperty(e => e.UpdatedAt, now), ct);

            if (affected == 1)
            {
                if (isRemoval)
                    audit.Deleted("StockEntry", entry.Id, item.Name);
                else
                    audit.TrackScalar("StockEntry", entry.Id, "Quantity",
                        Format(current), Format(request.NewQuantity));
                AppendAdjustmentMovement(item, request, delta, occurredAt);
                return;
            }

            if (attempt >= maxAttempts)
                throw new BusinessRuleException(
                    "The recorded quantity changed while you were correcting it — please try again.");
            // else: loop — FindEntryAsync re-reads the now-committed value (READ COMMITTED) and recomputes delta.
        }
    }

    private void AppendAdjustmentMovement(
        Item item, StockAdjustRequest request, decimal delta, DateTimeOffset occurredAt) =>
        AppendMovement(item, MovementKind.Adjustment,
            from: delta < 0 ? request.Location : null,
            to: delta > 0 ? request.Location : null,
            Math.Abs(delta), unitCost: null, unitCostCurrencyId: null, occurredAt,
            relatedItemId: null, request.Note, source: null);

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
            throw new BusinessRuleException(WholeNumberRequiredMessage);

        await ApplyDeltaAsync(itemId, request.From, -1m, ct, requireWholeNumber: true);

        AppendMovement(item, MovementKind.Conversion, from: request.From, to: null, 1m,
            unitCost: null, unitCostCurrencyId: null, occurredAt,
            relatedItemId: request.RelatedItemId, request.Note, source: null);
    }

    public async Task ConvertInAsync(Guid itemId, StockConvertInRequest request, CancellationToken ct)
    {
        var item = await LoadConvertibleItemAsync(itemId, ct);
        await ValidateLocationAsync(request.To, ct, isDestination: true);
        if (request.RelatedItemIds.Count == 0)
            throw new BusinessRuleException("At least one item must be selected to collapse.");
        var occurredAt = ResolveOccurredAt(request.OccurredAt);

        await ApplyDeltaAsync(itemId, request.To, request.RelatedItemIds.Count, ct);

        // One quantity-1 inflow per collapsed instance, each carrying its id — full two-way
        // traceability, symmetric with track-one.
        foreach (var relatedItemId in request.RelatedItemIds)
        {
            AppendMovement(item, MovementKind.Conversion, from: null, to: request.To, 1m,
                unitCost: null, unitCostCurrencyId: null, occurredAt, relatedItemId, request.Note,
                source: null);
        }
    }

    /// <summary>
    /// Applies a signed delta to the entry at a location, creating/merging on increment and
    /// deleting on reaching zero. Both directions are race-guarded database-level writes: an increment
    /// whose row was concurrently purged re-inserts (so the inflow never vanishes), and a decrement is a
    /// guarded atomic update that rejects an over-draw. Entry timestamps are record-time (<c>UtcNow</c>),
    /// never the user-suppliable <c>OccurredAt</c>. Pass <paramref name="requireWholeNumber"/> to also
    /// enforce a whole-number source quantity at the database (track-one).
    /// </summary>
    private async Task ApplyDeltaAsync(
        Guid itemId, StockLocation location, decimal delta, CancellationToken ct,
        bool requireWholeNumber = false)
    {
        if (delta == 0m)
            return;

        var now = DateTimeOffset.UtcNow;
        var entry = await FindEntryAsync(itemId, location, ct);

        if (delta > 0m)
        {
            if (entry is null)
            {
                AddNewEntry(itemId, location, delta, now);
            }
            else
            {
                var merged = await db.StockEntries.Where(e => e.Id == entry.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.Quantity, e => e.Quantity + delta)
                        .SetProperty(e => e.UpdatedAt, now), ct);
                if (merged == 0)
                {
                    // Concurrent decrement-to-zero purged the row after our read. Re-insert so the
                    // inflow lands; a concurrent re-insert at the same location loses on the NULLS NOT
                    // DISTINCT unique index (23505 -> 409 via DomainExceptionHandler).
                    AddNewEntry(itemId, location, delta, now);
                }
                else
                {
                    audit.TrackScalar("StockEntry", entry.Id, "Quantity",
                        Format(entry.Quantity), Format(entry.Quantity + delta));
                }
            }

            return;
        }

        var amount = -delta;
        if (entry is null)
            throw new BusinessRuleException("There is no stock of this item at the source location.");
        if (entry.Quantity < amount)
            throw new BusinessRuleException(
                $"Only {Format(entry.Quantity)} on hand at the source location — cannot remove {Format(amount)}.");

        // Guarded decrement: race-safe against a concurrent use and never drives the row negative. The
        // whole-number rule (track-one) is composed into the guard so it holds at the database under a
        // concurrent fractional consume, not just as the caller's advisory snapshot pre-check.
        var guarded = db.StockEntries.Where(e => e.Id == entry.Id && e.Quantity >= amount);
        if (requireWholeNumber)
            guarded = guarded.Where(e => e.Quantity % 1m == 0m);
        var affected = await guarded
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Quantity, e => e.Quantity - amount)
                .SetProperty(e => e.UpdatedAt, now), ct);
        if (affected == 0)
            throw new BusinessRuleException(requireWholeNumber
                ? WholeNumberRequiredMessage
                : "Not enough on hand at the source location.");

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
        string? note, string? source)
    {
        var trimmedNote = Normalize(note);
        if (trimmedNote is { Length: > NoteMaxLength })
            throw new BusinessRuleException($"Note must be {NoteMaxLength} characters or fewer.");

        var trimmedSource = Normalize(source);
        if (trimmedSource is { Length: > SourceMaxLength })
            throw new BusinessRuleException($"Source must be {SourceMaxLength} characters or fewer.");

        db.StockMovements.Add(new StockMovement
        {
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
            Source = trimmedSource,
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
            ItemId = itemId,
            AreaId = location.AreaId,
            ContainerItemId = location.ContainerItemId,
            Quantity = quantity,
            CreatedAt = now,
            UpdatedAt = now,
        };

    // Track-and-audit a fresh stock entry. Shared by the create/increment/re-insert branches of
    // ApplyDeltaAsync and AdjustAsync so the create-side audit convention lives in exactly one place.
    private void AddNewEntry(Guid itemId, StockLocation location, decimal quantity, DateTimeOffset now)
    {
        var created = NewEntry(itemId, location, quantity, now);
        db.StockEntries.Add(created);
        audit.Created("StockEntry", created.Id);
    }

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

    // Sources are existence-only (so stranded stock can still be reconciled out of a terminal
    // container); destinations must additionally be live. Pass <paramref name="isDestination"/> true
    // at every receive/transfer-to/convert-in call site.
    private async Task ValidateLocationAsync(StockLocation location, CancellationToken ct, bool isDestination = false)
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
        // Loose stock can only sit in a container — a non-container holds components, not a place that takes stock.
        if (container.Fungibility != Fungibility.NonFungible || container.IsContainer != true)
            throw new BusinessRuleException("ContainerItemId must reference a container item.");
        // A destination must be live: stock moves OUT of an archived container
        // but never INTO one — the complement of the durable sealing gate.
        if (isDestination && container.Status.IsTerminal())
            throw new BusinessRuleException("Cannot place stock into an archived container.");
    }

    // True only for a container location whose holder is archived; area
    // locations have no status. Used by AdjustAsync to block an adjust-UP inflow into a terminal
    // container while still allowing an adjust-DOWN reconcile-out. Assumes the location was already
    // validated (existence + container-kind) by ValidateLocationAsync.
    private async Task<bool> IsTerminalContainerAsync(StockLocation location, CancellationToken ct)
    {
        if (location.ContainerItemId is not { } containerId)
            return false;
        var status = await db.Items.AsNoTracking()
            .Where(i => i.Id == containerId)
            .Select(i => (ItemStatus?)i.Status)
            .FirstOrDefaultAsync(ct);
        return status is { } s && s.IsTerminal();
    }

    private async Task ValidateUnitCostAsync(decimal? unitCost, Guid? currencyId, CancellationToken ct)
    {
        if (unitCost.HasValue != currencyId.HasValue)
            throw new BusinessRuleException("UnitCost and UnitCostCurrencyId must be set together.");
        if (unitCost is < 0m)
            throw new BusinessRuleException("UnitCost must be zero or positive.");
        if (currencyId is { } id)
            await CurrencyValidation.EnsureCurrencyAsync(db, id, "UnitCostCurrencyId", ct);
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
        if (DateTimeOffsetValidationRules.IsInFuture(resolved))
            throw new BusinessRuleException("OccurredAt cannot be in the future.");
        return resolved;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
