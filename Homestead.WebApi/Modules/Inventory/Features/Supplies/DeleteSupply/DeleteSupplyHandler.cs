using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.DeleteSupply;

public class DeleteSupplyHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteSupplyCommand, bool>
{
    public async Task<bool> Handle(DeleteSupplyCommand request, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(
            i => i.Id == request.Id && i.Fungibility == Fungibility.Fungible, ct);
        if (item is null)
            return false;

        // Delete is the mistake-eraser; a supply that carries ledger history can only be archived.
        // Any stock entry implies a recorded movement, so the ledger check covers both.
        var hasHistory = await db.StockMovements.AnyAsync(m => m.ItemId == item.Id, ct);
        if (hasHistory)
            throw new BusinessRuleException("Supply has stock history and cannot be deleted. Archive it instead.");

        db.Items.Remove(item);
        audit.Deleted("Item", item.Id, item.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
