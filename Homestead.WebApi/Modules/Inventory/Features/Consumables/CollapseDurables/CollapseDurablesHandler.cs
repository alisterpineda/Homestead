using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CollapseDurables;

public class CollapseDurablesHandler(
    AppDbContext db,
    IStockService stock,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork,
    IValidator<CollapseDurablesCommand> validator)
{
    public async Task<ConsumableDetailResponse> Handle(CollapseDurablesCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var body = request.Body;

        // Every check runs before any mutation, so a rejected collapse leaves stock, the ledger, and
        // item statuses untouched.

        // Batch semantics: a missing id is a 400 over the whole set, not a 404 for one. Tracked load —
        // these rows are archived below.
        var items = await db.Items
            .Where(i => body.ItemIds.Contains(i.Id))
            .ToListAsync(ct);
        if (items.Count != body.ItemIds.Count)
            throw new BusinessRuleException("One or more selected items no longer exist.");

        if (items.Any(i => i.Fungibility != Fungibility.NonFungible))
            throw new BusinessRuleException("Only individually-tracked items can be collapsed.");

        // Lent items are physically elsewhere; collapsing them would inflate on-hand and silently
        // drop their status note.
        if (items.Any(i => i.Status != ItemStatus.Active))
            throw new BusinessRuleException("Only Active items can be collapsed back into stock.");

        if (body.ContainerItemId is { } target && body.ItemIds.Contains(target))
            throw new BusinessRuleException("An item being collapsed cannot be the destination container.");

        // Sealing-container gate (mirrors PatchDurableHandler's terminal-sealing rule, which only
        // blocks a container): a container being collapsed must be empty. A non-container's components
        // ride along — collapsing one is not blocked by them — so only containers are checked.
        var containers = items.Where(i => i.IsContainer == true).ToList();
        if (containers.Count > 0)
        {
            var containerIds = containers.Select(i => i.Id).ToList();

            var liveChildParentIds = await db.Items.AsNoTracking()
                .Where(i => i.ParentItemId != null
                    && containerIds.Contains(i.ParentItemId.Value)
                    && !ItemStatusExtensions.TerminalStatuses.Contains(i.Status))
                .Select(i => i.ParentItemId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var stockedContainerIds = await db.StockEntries.AsNoTracking()
                .Where(e => e.ContainerItemId != null && containerIds.Contains(e.ContainerItemId.Value))
                .Select(e => e.ContainerItemId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var blocked = containers.FirstOrDefault(c =>
                liveChildParentIds.Contains(c.Id) || stockedContainerIds.Contains(c.Id));
            if (blocked is not null)
                throw new BusinessRuleException(
                    $"Cannot collapse '{blocked.Name}' while it has active contents.");
        }

        await stock.ConvertInAsync(request.ConsumableId, new StockConvertInRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            body.ItemIds, body.OccurredAt, body.Note), ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var item in items)
        {
            // Mirror the live→terminal transition lock: archive in place, keeping the item's location
            // (area / parent / compartment) for the moving-leftovers escape hatch.
            audit.Track<ItemSnapshot, Item>(item, "Item", item.Id);
            item.Status = ItemStatus.Archived;
            item.StatusNote = null;
            item.UpdatedAt = now;
            item.UpdatedByUserId = currentUser.UserId;
        }

        await unitOfWork.SaveChangesAsync(ct);

        return await ConsumableDetailReader.LoadAsync(db, locationResolver, request.ConsumableId, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");
    }
}
