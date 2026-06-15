using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.DeleteDurable;

public class DeleteDurableHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteDurableCommand, bool>
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
        // be archived. Contents are nested items or fungible stock entries pointing at this container.
        var hasChildren = await db.Items.AnyAsync(i => i.ParentItemId == item.Id, ct);
        if (hasChildren)
            throw new BusinessRuleException("Item has contents and cannot be deleted. Archive it instead.");

        var hasStock = await db.StockEntries.AnyAsync(e => e.ContainerItemId == item.Id, ct);
        if (hasStock)
            throw new BusinessRuleException("Item holds stock and cannot be deleted. Move or use it up first.");

        db.Items.Remove(item);
        audit.Deleted("Item", item.Id, item.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
