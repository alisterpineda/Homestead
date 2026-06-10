using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.PatchArea;

public class PatchAreaHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<PatchAreaCommand, AreaResponse>
{
    public async Task<AreaResponse> Handle(PatchAreaCommand request, CancellationToken ct)
    {
        var area = await db.Areas.FirstOrDefaultAsync(a => a.Id == request.Id, ct);
        if (area is null)
            throw new EntityNotFoundException("Area not found.");

        audit.Track<AreaSnapshot, Area>(area, "Area", area.Id);

        var model = new PatchAreaModel { Name = area.Name };
        request.PatchDocument.ApplyOrThrow(model);

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");
        if (model.Name.Length > 128)
            throw new BusinessRuleException("Name must be 128 characters or fewer.");

        area.Name = model.Name.Trim();
        area.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);

        var liveItemCount = await db.Items
            .AsNoTracking()
            .CountAsync(i => i.AreaId == area.Id
                && ItemStatusExtensions.LiveStatuses.Contains(i.Status), ct);

        return AreaResponseMapper.ToResponse(area, liveItemCount);
    }
}
