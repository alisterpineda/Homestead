using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;

public class CreateAreaHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateAreaCommand, AreaResponse>
{
    public async Task<AreaResponse> Handle(CreateAreaCommand request, CancellationToken ct)
    {
        var area = new Area
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Areas.Add(area);
        audit.Created("Area", area.Id);

        await unitOfWork.SaveChangesAsync(ct);

        return AreaResponseMapper.ToResponse(area, liveItemCount: 0);
    }
}
