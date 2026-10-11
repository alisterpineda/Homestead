using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.CreateBrand;

public class CreateBrandHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork, IValidator<CreateBrandCommand> validator)
{
    public async Task<BrandResponse> HandleAsync(CreateBrandCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        // Find-or-create (modelled on TagPathResolver.ResolveOrCreateAsync, not CreateParty which
        // 409s): a case-/whitespace-variant of an existing brand returns the existing row so the
        // endpoint stays idempotent. The unique lower("Name") index is the backstop.
        var name = request.Name.Trim();
        var nameLower = name.ToLower();

        var existing = await db.Brands
            .FirstOrDefaultAsync(b => b.Name.ToLower() == nameLower, ct);
        if (existing is not null)
        {
            // Find-or-create hit: report the existing row's real count so the response is consistent
            // with what the list/patch/merge handlers return for the same brand.
            var existingCount = await db.Items.CountAsync(i => i.BrandId == existing.Id, ct);
            return BrandResponseMapper.ToResponse(existing, existingCount);
        }

        var brand = new Brand
        {
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Brands.Add(brand);
        audit.Created("Brand", brand.Id);

        await unitOfWork.SaveChangesAsync(ct);

        return BrandResponseMapper.ToResponse(brand, 0);
    }
}
