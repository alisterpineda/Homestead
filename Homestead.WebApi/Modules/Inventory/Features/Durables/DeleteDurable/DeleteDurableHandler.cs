using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.DeleteDurable;

public class DeleteDurableHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<bool> Handle(DeleteDurableCommand request, CancellationToken ct)
    {
        // This endpoint is the non-fungible items surface; a fungible consumable id is "not found" here
        // (consumables delete through DeleteConsumable, which gates on ledger history). Without this filter a
        // consumable with movements would reach Remove and trip the StockMovement Restrict FK as a raw 500.
        var item = await db.Items.FirstOrDefaultAsync(
            i => i.Id == request.Id && i.Fungibility == Fungibility.NonFungible, ct);
        if (item is null)
            return false;

        // Delete is the mistake-eraser; an item that has held contents carries history and can only
        // be archived. Contents are nested items — stored-in children of a container or components of
        // an assembly — or fungible stock entries pointing at this container (the ParentItemId FK is Restrict).
        var hasChildren = await db.Items.AnyAsync(i => i.ParentItemId == item.Id, ct);
        if (hasChildren)
            throw new BusinessRuleException("Item has contents or components and cannot be deleted. Archive it instead.");

        var hasStock = await db.StockEntries.AnyAsync(e => e.ContainerItemId == item.Id, ct);
        if (hasStock)
            throw new BusinessRuleException("Item holds stock and cannot be deleted. Move or use it up first.");

        // A durable carved out of a consumable (track-one) is the RelatedItem of its Conversion
        // movement. The RelatedItemId FK is SetNull, so deleting the durable would silently sever that
        // back-reference from the ledger — gate it, mirroring DeleteConsumable's ledger check.
        var isTrackedInLedger = await db.StockMovements.AnyAsync(m => m.RelatedItemId == item.Id, ct);
        if (isTrackedInLedger)
            throw new BusinessRuleException(
                "Item is referenced by stock history (it was tracked from a consumable) and cannot be deleted. Archive it instead.");

        // Notes are user-authored history (activity.md §Delete gate). The FK cascades, so this handler check
        // is the friendly guard; archive is the way to retire an item and keep its story.
        var hasNotes = await db.ItemNotes.AnyAsync(n => n.ItemId == item.Id, ct);
        if (hasNotes)
            throw new BusinessRuleException("Item has notes and cannot be deleted. Delete its notes first, or archive it instead.");

        db.Items.Remove(item);
        audit.Deleted("Item", item.Id, item.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
