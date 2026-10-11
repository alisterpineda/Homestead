using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.PatchArea;

public class PatchAreaHandler(
    AppDbContext db,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    IInventoryPlacementService placementService)
{
    public async Task<AreaResponse> HandleAsync(Guid id, JsonPatchDocument<PatchAreaModel> patchDoc, CancellationToken ct)
    {
        var area = await db.Areas.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (area is null)
            throw new EntityNotFoundException("Area not found.");

        audit.Track<AreaSnapshot, Area>(area, "Area", area.Id);

        var model = new PatchAreaModel { Name = area.Name, ParentAreaId = area.ParentAreaId };
        patchDoc.ApplyOrThrow(model);

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");
        if (model.Name.Length > 128)
            throw new BusinessRuleException("Name must be 128 characters or fewer.");

        // Re-parenting (move-with-contents is automatic — resolution is derived, nothing cascades).
        // A null parent promotes to root. The cycle guard mirrors PatchDurableHandler's container check.
        if (model.ParentAreaId != area.ParentAreaId)
        {
            if (model.ParentAreaId is { } newParentId)
            {
                if (newParentId == area.Id)
                    throw new BusinessRuleException("An area cannot be its own parent.");

                var parent = await db.Areas.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == newParentId, ct);
                if (parent is null)
                    throw new BusinessRuleException("ParentAreaId must reference an existing area.");

                // An area cannot be moved under itself or any of its own descendants: walk up from
                // the proposed parent and reject if the chain reaches this area.
                var areaLinks = await db.Areas.AsNoTracking()
                    .Select(a => new { a.Id, a.ParentAreaId })
                    .ToDictionaryAsync(a => a.Id, a => a.ParentAreaId, ct);

                var visited = new HashSet<Guid>();
                var cursor = parent.ParentAreaId;
                while (cursor is { } ancestorId)
                {
                    if (ancestorId == area.Id)
                        throw new BusinessRuleException("An area cannot be moved inside one of its own descendants.");
                    if (!visited.Add(ancestorId) || !areaLinks.TryGetValue(ancestorId, out var next))
                        break;
                    cursor = next;
                }
            }

            area.ParentAreaId = model.ParentAreaId;
        }

        area.Name = model.Name.Trim();
        area.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);

        // Same resolved count as GetAreas — the field means one thing regardless of which
        // endpoint produced the response.
        var placement = await placementService.ComputeAsync(ct);

        var parentAreaName = area.ParentAreaId is { } pid
            ? await db.Areas.Where(a => a.Id == pid).Select(a => a.Name).FirstOrDefaultAsync(ct)
            : null;

        return AreaResponseMapper.ToResponse(
            area, placement.AreaItemCounts.GetValueOrDefault(area.Id), parentAreaName);
    }
}
