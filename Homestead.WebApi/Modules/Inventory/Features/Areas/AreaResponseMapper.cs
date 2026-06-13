using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas;

internal static class AreaResponseMapper
{
    public static AreaResponse ToResponse(Area a, int liveItemCount, string? parentAreaName = null) => new()
    {
        Id = a.Id,
        Name = a.Name,
        ParentAreaId = a.ParentAreaId,
        ParentAreaName = parentAreaName,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
        LiveItemCount = liveItemCount,
    };
}
