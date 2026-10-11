using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.PatchBrand;

public class PatchBrandHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<BrandResponse> HandleAsync(Guid id, JsonPatchDocument<PatchBrandModel> patchDoc, CancellationToken ct)
    {
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brand is null)
            throw new EntityNotFoundException("Brand not found.");

        audit.Track<BrandSnapshot, Brand>(brand, "Brand", brand.Id);

        var model = new PatchBrandModel { Name = brand.Name };
        patchDoc.ApplyOrThrow(model);

        // Validate the trimmed value — that is what persists — so a name padded with whitespace to >128
        // but trimming to a valid length isn't wrongly rejected on the raw JSON-patch endpoint.
        var name = model.Name.Trim();
        if (name.Length == 0)
            throw new BusinessRuleException("Name is required.");
        if (name.Length > 128)
            throw new BusinessRuleException("Name must be 128 characters or fewer.");

        // Rename-uniqueness (PatchTag pattern; entry-time find-or-create only dedups on create). A
        // collision is a 409, which the edit dialog turns into a "merge into it?" offer — rename can't
        // consolidate onto an existing name, merge can.
        var nameLower = name.ToLower();
        if (await db.Brands.AnyAsync(b => b.Name.ToLower() == nameLower && b.Id != id, ct))
            throw new DuplicateEntityException($"A brand named '{name}' already exists.");

        brand.Name = name;
        brand.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);

        var itemCount = await db.Items.CountAsync(i => i.BrandId == brand.Id, ct);
        return BrandResponseMapper.ToResponse(brand, itemCount);
    }
}
