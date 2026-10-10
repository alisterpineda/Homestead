using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.DeleteArea;

public class DeleteAreaHandler(
    AppDbContext db,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    IInventoryPlacementService placementService)
{
    public async Task<bool> Handle(DeleteAreaCommand request, CancellationToken ct)
    {
        var area = await db.Areas.FirstOrDefaultAsync(a => a.Id == request.Id, ct);
        if (area is null)
            return false;

        // Sub-areas block deletion (a friendly message ahead of the FK Restrict backstop): a parent
        // can't be removed out from under its children. Checked first — it's the cheapest guard.
        var hasChildren = await db.Areas.AnyAsync(a => a.ParentAreaId == area.Id, ct);
        if (hasChildren)
            throw new BusinessRuleException("Area has sub-areas and cannot be deleted. Move or delete them first.");

        // Anything live RESOLVING to the area blocks deletion — the same count the areas table
        // shows and the delete button gates on, so the UI and this rule can't disagree. Terminal
        // items don't block (the FK is SetNull, so their stale label clears on delete).
        var placement = await placementService.ComputeAsync(ct);
        if (placement.AreaItemCounts.GetValueOrDefault(area.Id) > 0)
            throw new BusinessRuleException("Area has items in it and cannot be deleted. Move or retire them first.");

        // Stock currently sitting in the area also blocks deletion (a stock entry exists only while
        // there is quantity there) — status-agnostic, unlike the resolved count: an ARCHIVED consumable's
        // stock is hidden from counts/lists but must still block, since a stock entry requires
        // exactly one of AreaId/ContainerItemId and SetNull would corrupt it. Historical movements
        // through the area survive via SET NULL.
        var hasStock = await db.StockEntries.AnyAsync(e => e.AreaId == area.Id, ct);
        if (hasStock)
            throw new BusinessRuleException("Area has stock in it and cannot be deleted. Move or use it up first.");

        db.Areas.Remove(area);
        audit.Deleted("Area", area.Id, area.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
