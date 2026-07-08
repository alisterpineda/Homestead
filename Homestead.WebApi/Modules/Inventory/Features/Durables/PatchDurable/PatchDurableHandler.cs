using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
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
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchDurableCommand, DurableResponse>
{
    public async Task<DurableResponse> Handle(PatchDurableCommand request, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == request.Id, ct);
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
            Value = item.Value,
            ValueCurrencyId = item.ValueCurrencyId,
            Status = item.Status,
            LentTo = item.LentTo,
            AreaId = item.AreaId,
            ParentItemId = item.ParentItemId,
            DurableKind = item.DurableKind,
            Compartment = item.Compartment,
        };

        request.PatchDocument.ApplyOrThrow(model);

        // JsonPatch binds arbitrary integers to the enum-typed fields (allowIntegerValues defaults
        // true), so an out-of-range Status/DurableKind would persist via HasConversion<string>() and
        // later re-serialize as a JSON number, breaking the TS string-union types. Reject them here.
        if (!Enum.IsDefined(model.Status))
            throw new BusinessRuleException("Status is not a valid item status.");
        if (model.DurableKind is { } patchedKind && !Enum.IsDefined(patchedKind))
            throw new BusinessRuleException("DurableKind is not a valid value.");

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
        {
            var currency = await db.Commodities.FirstOrDefaultAsync(c => c.Id == currencyId, ct);
            if (currency is null)
                throw new BusinessRuleException("ValueCurrencyId must reference an existing commodity.");
            if (currency.Kind != CommodityKind.Currency)
                throw new BusinessRuleException("ValueCurrencyId must reference a currency commodity.");
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
            // Typing: a parent must be a non-fungible holder (container or assembly).
            var parent = await db.Items.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == newParentId, ct);
            if (parent is null)
                throw new BusinessRuleException("ParentItemId must reference an existing item.");
            if (parent.Fungibility != Fungibility.NonFungible || !parent.DurableKind.IsHolder())
                throw new BusinessRuleException("ParentItemId must reference a container or assembly item.");
            // Things move OUT of a terminal holder but never INTO one — unconditional on the moved
            // item's own status (block universally). A patch that both re-parents and goes terminal
            // already freezes location above, so this only guards a live-item re-parent.
            if (parent.Status.IsTerminal())
                throw new BusinessRuleException("ParentItemId must reference a live container or assembly.");

            // Cycle: an item cannot be placed inside itself or inside any of its own descendants.
            if (newParentId == item.Id)
                throw new BusinessRuleException("An item cannot be placed inside itself.");

            // A cycle can form through either edge kind (stored-in or component), so the link set
            // spans all holders, not just containers.
            var holderLinks = await db.Items.AsNoTracking()
                .Where(i => i.DurableKind == DurableKind.Container || i.DurableKind == DurableKind.Assembly)
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

        // A non-fungible item always names a holder-role; a patch may switch it but never clear it.
        if (model.DurableKind is not { } targetKind)
            throw new BusinessRuleException("DurableKind is required.");

        // Children gates — one query, only when a relevant change is requested.
        var kindChange = targetKind != item.DurableKind;
        // Only a Container seals against its contents; an Assembly's components ride along (their
        // effective state is derived at read time, like a lent container), so it never blocks.
        var sealingContainer = transitioningToTerminal && item.DurableKind == DurableKind.Container;
        if (kindChange || sealingContainer)
        {
            var childStatuses = await db.Items.AsNoTracking()
                .Where(i => i.ParentItemId == item.Id)
                .Select(i => i.Status)
                .ToListAsync(ct);

            // Fungible stock pointing at this container is live contents — it joins both gates. A
            // stock entry exists only while there is quantity there, so its mere presence blocks.
            var hasStock = await db.StockEntries.AnyAsync(e => e.ContainerItemId == item.Id, ct);

            // Kind-change gate: changing the holder-role is blocked while the item still references
            // ANY child — live or terminal — or any stock sits in it. Empty it first. (A Plain item
            // holds nothing, so Plain → Container/Assembly is always free.) Generalises the old role-off rule.
            if (kindChange && (childStatuses.Count > 0 || hasStock))
                throw new BusinessRuleException(
                    "Cannot change the type while the item still has contents (including inactive records). Empty it first.");

            // Taking a container terminal: only LIVE children block; terminal leftovers don't, so
            // the kind stays Container and the parent-is-holder invariant is preserved. Live stock blocks too.
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
        item.Value = model.Value;
        item.ValueCurrencyId = model.ValueCurrencyId;
        item.Status = model.Status;
        item.LentTo = lentTo;
        item.AreaId = targetAreaId;
        item.ParentItemId = targetParentId;
        item.DurableKind = targetKind;
        item.Compartment = targetCompartment;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedByUserId = currentUser.UserId;

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
