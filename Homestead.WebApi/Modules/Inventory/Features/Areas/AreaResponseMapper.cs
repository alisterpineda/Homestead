using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas;

internal static class AreaResponseMapper
{
    public static AreaResponse ToResponse(Area a, int liveItemCount) => new()
    {
        Id = a.Id,
        Name = a.Name,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
        LiveItemCount = liveItemCount,
    };
}
