using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.DeleteConsumable;

public class DeleteConsumableHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteConsumableCommand, bool>
{
    public async Task<bool> Handle(DeleteConsumableCommand request, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(
            i => i.Id == request.Id && i.Fungibility == Fungibility.Fungible, ct);
        if (item is null)
            return false;

        // Delete is the mistake-eraser; a consumable that carries ledger history can only be archived.
        // Any stock entry implies a recorded movement, so the ledger check covers both.
        var hasHistory = await db.StockMovements.AnyAsync(m => m.ItemId == item.Id, ct);
        if (hasHistory)
            throw new BusinessRuleException("Consumable has stock history and cannot be deleted. Archive it instead.");

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
