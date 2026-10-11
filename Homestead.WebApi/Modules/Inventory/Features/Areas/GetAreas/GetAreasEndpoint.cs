using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.GetAreas;

public static class GetAreasEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetAreasQuery request,
            GetAreasHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("GetAreas")
        .Produces<PaginatedResponse<AreaResponse>>();
    }
}
