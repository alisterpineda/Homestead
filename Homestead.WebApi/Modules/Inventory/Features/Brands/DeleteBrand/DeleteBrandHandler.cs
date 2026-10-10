using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.DeleteBrand;

public class DeleteBrandHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<bool> Handle(DeleteBrandCommand request, CancellationToken ct)
    {
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, ct);
        if (brand is null)
            return false;

        // In-use guard (a friendly message ahead of the FK Restrict backstop): a brand referenced by any
        // item can't be deleted. Merge is how you retire an in-use brand — it re-points the items first.
        if (await db.Items.AnyAsync(i => i.BrandId == brand.Id, ct))
            throw new BusinessRuleException(
                "Brand is in use by one or more items and cannot be deleted. " +
                "Merge it into another brand or remove it from those items first.");

        db.Brands.Remove(brand);
        audit.Deleted("Brand", brand.Id, brand.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
