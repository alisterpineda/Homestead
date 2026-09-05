using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.DeleteWarranty;

public class DeleteItemWarrantyHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteItemWarrantyCommand, bool>
{
    public async Task<bool> Handle(DeleteItemWarrantyCommand request, CancellationToken ct)
    {
        var warranty = await db.ItemWarranties.FirstOrDefaultAsync(
            w => w.Id == request.WarrantyId && w.ItemId == request.ItemId, ct);
        if (warranty is null)
            return false;

        // A real delete — no trash, no tombstone. The audit row keeps a one-line description so the
        // removal is still traceable in the item's feed.
        db.ItemWarranties.Remove(warranty);
        audit.Deleted("ItemWarranty", warranty.Id,
            ItemWarrantyText.Describe(warranty), parentEntityId: warranty.ItemId);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
