using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.PatchDurable;

public class PatchDurableHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IItemLocationResolver locationResolver,
    IInventoryTagPathResolver tagResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchDurableCommand, DurableResponse>
{
    public async Task<DurableResponse> Handle(PatchDurableCommand request, CancellationToken ct)
    {
        var item = await db.Items
            .Include(i => i.ItemTags).ThenInclude(it => it.Tag)
            .FirstOrDefaultAsync(i => i.Id == request.Id, ct);
        if (item is null)
            throw new EntityNotFoundException("Item not found.");

        // v1 surfaces non-fungible durables only.
        if (item.Fungibility != Fungibility.NonFungible)
            throw new BusinessRuleException("Only non-fungible items can be edited.");

        audit.Track<ItemSnapshot, Item>(item, "Item", item.Id);

        var model = new PatchDurableModel
        {
            Name = item.Name,
            Notes = item.Notes,
            Serial = item.Serial,
            BrandId = item.BrandId,
            Model = item.Model,
            AcquiredOn = item.AcquiredOn,
            AcquisitionKind = item.AcquisitionKind,
            AcquisitionCost = item.AcquisitionCost,
            AcquisitionCostCurrencyId = item.AcquisitionCostCurrencyId,
            AcquiredFrom = item.AcquiredFrom,
            Value = item.Value,
            ValueCurrencyId = item.ValueCurrencyId,
            Status = item.Status,
            LentTo = item.LentTo,
            AreaId = item.AreaId,
            ParentItemId = item.ParentItemId,
            IsContainer = item.IsContainer,
            Compartment = item.Compartment,
            Tags = item.ItemTags.Select(it => it.Tag.Path).ToList(),
        };

        request.PatchDocument.ApplyOrThrow(model);

        // JsonPatch binds arbitrary integers to the enum-typed fields (allowIntegerValues defaults
        // true), so an out-of-range Status would persist via HasConversion<string>() and later
        // re-serialize as a JSON number, breaking the TS string-union types. Reject it here. (IsContainer
        // is a plain bool now — no range check needed.)
        if (!Enum.IsDefined(model.Status))
            throw new BusinessRuleException("Status is not a valid item status.");
        if (model.AcquisitionKind is { } patchedAcquisitionKind && !Enum.IsDefined(patchedAcquisitionKind))
            throw new BusinessRuleException("AcquisitionKind is not a valid value.");

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");
        if (model.Name.Length > 256)
            throw new BusinessRuleException("Name must be 256 characters or fewer.");
        if (model.Serial is { Length: > 128 })
            throw new BusinessRuleException("Serial must be 128 characters or fewer.");
        if (model.Model is { Length: > 128 })
            throw new BusinessRuleException("Model must be 128 characters or fewer.");
        if (model.LentTo is { Length: > 256 })
            throw new BusinessRuleException("LentTo must be 256 characters or fewer.");
        if (model.AcquiredFrom is { Length: > 256 })
            throw new BusinessRuleException("AcquiredFrom must be 256 characters or fewer.");
        if (model.Compartment is { Length: > 256 })
            throw new BusinessRuleException("Compartment must be 256 characters or fewer.");

        if (model.AcquiredOn is { } acquired && DateOnlyValidationRules.IsInFuture(acquired))
            throw new BusinessRuleException("AcquiredOn cannot be in the future.");

        // Value and its currency are set together or not at all.
        if (model.Value.HasValue != model.ValueCurrencyId.HasValue)
            throw new BusinessRuleException("Value and ValueCurrencyId must be set together.");
        if (model.Value is < 0)
            throw new BusinessRuleException("Value must be zero or positive.");
        if (model.ValueCurrencyId is { } currencyId)
            await CurrencyValidation.EnsureCurrencyAsync(db, currencyId, "ValueCurrencyId", ct);

        // Acquisition cost and its currency are set together or not at all.
        if (model.AcquisitionCost.HasValue != model.AcquisitionCostCurrencyId.HasValue)
            throw new BusinessRuleException("AcquisitionCost and AcquisitionCostCurrencyId must be set together.");
        if (model.AcquisitionCost is < 0)
            throw new BusinessRuleException("AcquisitionCost must be zero or positive.");

        // Same currency as the value pair ⇒ the lookup above already validated it.
        if (model.AcquisitionCostCurrencyId is { } acquisitionCurrencyId
            && acquisitionCurrencyId != model.ValueCurrencyId)
        {
            await CurrencyValidation.EnsureCurrencyAsync(db, acquisitionCurrencyId, "AcquisitionCostCurrencyId", ct);
        }

        if (model.BrandId is { } brandId && brandId != item.BrandId)
        {
            var brandExists = await db.Brands.AnyAsync(b => b.Id == brandId, ct);
            if (!brandExists)
                throw new BusinessRuleException("BrandId must reference an existing brand.");
        }

        // Lent requires a recipient; any other status clears the LentTo note.
        string? lentTo;
        if (model.Status == ItemStatus.Lent)
        {
            if (string.IsNullOrWhiteSpace(model.LentTo))
                throw new BusinessRuleException("LentTo is required when the status is Lent.");
            lentTo = model.LentTo.Trim();
        }
        else
        {
            lentTo = null;
        }

        // Transition lock: a live→terminal transition keeps the item's existing location (area,
        // parent, compartment) — location ops in the same patch are ignored. An item that is
        // ALREADY terminal applies location edits normally (the escape hatch for moving leftovers).
        var transitioningToTerminal = !item.Status.IsTerminal() && model.Status.IsTerminal();
        var targetAreaId = transitioningToTerminal ? item.AreaId : model.AreaId;
        var targetParentId = transitioningToTerminal ? item.ParentItemId : model.ParentItemId;
        var targetCompartment = transitioningToTerminal ? item.Compartment : model.Compartment;

        // At most one location: an item sits in an area, in a container, or nowhere.
        if (targetAreaId is not null && targetParentId is not null)
            throw new BusinessRuleException("An item can be in an area or a container, not both.");

        if (targetAreaId is { } areaId && areaId != item.AreaId)
        {
            var areaExists = await db.Areas.AnyAsync(a => a.Id == areaId, ct);
            if (!areaExists)
                throw new BusinessRuleException("AreaId must reference an existing area.");
        }

        if (targetParentId is { } newParentId && newParentId != item.ParentItemId)
        {
            // Typing: any live non-fungible item can be a parent — a container stores this item, a
            // regular item takes it as a component.
            var parent = await db.Items.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == newParentId, ct);
            if (parent is null)
                throw new BusinessRuleException("ParentItemId must reference an existing item.");
            if (parent.Fungibility != Fungibility.NonFungible)
                throw new BusinessRuleException("ParentItemId must reference a non-fungible item.");
            // Things move OUT of a terminal parent but never INTO one — unconditional on the moved
            // item's own status (block universally). A patch that both re-parents and goes terminal
            // already freezes location above, so this only guards a live-item re-parent.
            if (parent.Status.IsTerminal())
                throw new BusinessRuleException("ParentItemId must reference a live item.");

            // Cycle: an item cannot be placed inside itself or inside any of its own descendants.
            if (newParentId == item.Id)
                throw new BusinessRuleException("An item cannot be placed inside itself.");

            // A cycle can form through either edge kind (stored-in or component), so the link set
            // spans every non-fungible item — any of them can be a chain node.
            var holderLinks = await db.Items.AsNoTracking()
                .Where(i => i.Fungibility == Fungibility.NonFungible)
                .Select(i => new { i.Id, i.ParentItemId })
                .ToDictionaryAsync(i => i.Id, i => i.ParentItemId, ct);

            var visited = new HashSet<Guid>();
            var cursor = parent.ParentItemId;
            while (cursor is { } ancestorId)
            {
                if (ancestorId == item.Id)
                    throw new BusinessRuleException("An item cannot be placed inside one of its own descendants.");
                if (!visited.Add(ancestorId) || !holderLinks.TryGetValue(ancestorId, out var next))
                    break;
                cursor = next;
            }
        }

        // A non-fungible item always has a container flag; a patch may flip it but a cleared (null)
        // /isContainer keeps the current value rather than violating the not-null invariant.
        var targetIsContainer = model.IsContainer ?? item.IsContainer ?? false;

        // Children gates — one query, only when a relevant change is requested.
        var containerChange = targetIsContainer != item.IsContainer;
        // Only a container seals against its contents; a non-container's components ride along (their
        // effective state is derived at read time, like a lent container), so it never blocks.
        var sealingContainer = transitioningToTerminal && item.IsContainer == true;
        if (containerChange || sealingContainer)
        {
            var childStatuses = await db.Items.AsNoTracking()
                .Where(i => i.ParentItemId == item.Id)
                .Select(i => i.Status)
                .ToListAsync(ct);

            // Fungible stock pointing at this container is live contents — it joins both gates. A
            // stock entry exists only while there is quantity there, so its mere presence blocks.
            var hasStock = await db.StockEntries.AnyAsync(e => e.ContainerItemId == item.Id, ct);

            // Single flip guard (both directions): IsContainer can change only while the item holds
            // nothing — ANY child (live or terminal) or any stock blocks it. A true→false flip is
            // locked by stored contents; a false→true flip is locked by components. Either way the
            // flip would reinterpret existing children, so it is refused. Empty it first.
            if (containerChange && (childStatuses.Count > 0 || hasStock))
                throw new BusinessRuleException(
                    "Cannot change whether this is a container while it still holds anything (including inactive records). Empty it first.");

            // Taking a container terminal: only LIVE children block; terminal leftovers don't, so
            // it stays a container and the parent invariant is preserved. Live stock blocks too.
            if (sealingContainer && (childStatuses.Any(s => !s.IsTerminal()) || hasStock))
                throw new BusinessRuleException(
                    "Cannot mark a container Sold/Discarded/Archived while it has active contents.");
        }

        // Compartment is a sub-position within a container — silently cleared when there is no parent.
        targetCompartment = targetParentId is null ? null : Normalize(targetCompartment);

        item.Name = model.Name.Trim();
        item.Notes = Normalize(model.Notes);
        item.Serial = Normalize(model.Serial);
        item.BrandId = model.BrandId;
        item.Model = Normalize(model.Model);
        item.AcquiredOn = model.AcquiredOn;
        item.AcquisitionKind = model.AcquisitionKind;
        item.AcquisitionCost = model.AcquisitionCost;
        item.AcquisitionCostCurrencyId = model.AcquisitionCostCurrencyId;
        item.AcquiredFrom = Normalize(model.AcquiredFrom);
        item.Value = model.Value;
        item.ValueCurrencyId = model.ValueCurrencyId;
        item.Status = model.Status;
        item.LentTo = lentTo;
        item.AreaId = targetAreaId;
        item.ParentItemId = targetParentId;
        item.IsContainer = targetIsContainer;
        item.Compartment = targetCompartment;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedByUserId = currentUser.UserId;

        // Whole-set replace: model.Tags is seeded from the item's current paths, so an omitted /tags
        // op is a no-op; a replace swaps the entire set (ancestors upserted).
        // `?? []`: JsonPatch `replace /tags null` silently nulls the List (no ApplyOrThrow error), and
        // `null.Where(...)` would throw ArgumentNullException → unhandled 500. Treat null as "clear".
        var tagPaths = (model.Tags ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var resolvedTags = await tagResolver.ResolveOrCreateManyAsync(tagPaths, currentUser.UserId, ct);
        InventoryItemTagSync.SetItemTags(item, resolvedTags);

        await unitOfWork.SaveChangesAsync(ct);

        var response = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(DurableResponseMapper.Projection)
            .FirstAsync(ct);

        return await locationResolver.EnrichAsync(response, ct);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
