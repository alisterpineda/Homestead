using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;

public class CreateAreaHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork, IValidator<CreateAreaCommand> validator)
{
    public async Task<AreaResponse> Handle(CreateAreaCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        // A new area can't yet have descendants, so no cycle is possible — only existence matters.
        // BusinessRuleException (400) for a bad reference, matching PatchArea and the item handlers.
        string? parentAreaName = null;
        if (request.ParentAreaId is { } parentId)
        {
            parentAreaName = await db.Areas
                .Where(a => a.Id == parentId)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(ct);
            if (parentAreaName is null)
                throw new BusinessRuleException("ParentAreaId must reference an existing area.");
        }

        var area = new Area
        {
            Name = request.Name.Trim(),
            ParentAreaId = request.ParentAreaId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Areas.Add(area);
        audit.Created("Area", area.Id);

        await unitOfWork.SaveChangesAsync(ct);

        return AreaResponseMapper.ToResponse(area, liveItemCount: 0, parentAreaName);
    }
}
