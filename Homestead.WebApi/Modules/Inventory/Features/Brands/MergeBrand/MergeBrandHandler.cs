using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.MergeBrand;

public class MergeBrandHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<BrandResponse> Handle(MergeBrandCommand request, CancellationToken ct)
    {
        if (request.SourceId == request.TargetId)
            throw new BusinessRuleException("A brand cannot be merged into itself.");

        var source = await db.Brands.FirstOrDefaultAsync(b => b.Id == request.SourceId, ct);
        if (source is null)
            throw new EntityNotFoundException("Source brand not found.");

        var target = await db.Brands.FirstOrDefaultAsync(b => b.Id == request.TargetId, ct);
        if (target is null)
            throw new EntityNotFoundException("Target brand not found.");

        // Re-point every item on the source onto the target (DeleteGroupHandler FK-reassign precedent):
        // the bulk ExecuteUpdate shares the command transaction automatically with the tracked
        // Brands.Remove + flush below, so the re-point and the deletion are atomic without a manual tx.
        // As with DeleteGroup nulling document GroupId, the per-item BrandId change is not field-audited;
        // the brand deletion is.
        await db.Items
            .Where(i => i.BrandId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.BrandId, target.Id), ct);

        audit.Deleted("Brand", source.Id, source.Name);
        db.Brands.Remove(source);

        await unitOfWork.SaveChangesAsync(ct);

        var itemCount = await db.Items.CountAsync(i => i.BrandId == target.Id, ct);
        return BrandResponseMapper.ToResponse(target, itemCount);
    }
}
