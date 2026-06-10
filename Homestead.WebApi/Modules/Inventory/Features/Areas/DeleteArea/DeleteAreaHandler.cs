using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.DeleteArea;

public class DeleteAreaHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteAreaCommand, bool>
{
    public async Task<bool> Handle(DeleteAreaCommand request, CancellationToken ct)
    {
        var area = await db.Areas.FirstOrDefaultAsync(a => a.Id == request.Id, ct);
        if (area is null)
            return false;

        // Live (non-terminal) items directly in the area block deletion. Terminal items don't —
        // the FK is SetNull, so their stale label clears on delete.
        var hasLiveItems = await db.Items
            .AnyAsync(i => i.AreaId == area.Id
                && ItemStatusExtensions.LiveStatuses.Contains(i.Status), ct);
        if (hasLiveItems)
            throw new BusinessRuleException("Area has items in it and cannot be deleted. Move or retire them first.");

        db.Areas.Remove(area);
        audit.Deleted("Area", area.Id, area.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
