using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurables;

public static class GetDurablesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetDurablesQuery request,
            GetDurablesHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("GetDurables")
        .Produces<PaginatedResponse<DurableResponse>>();
    }
}
