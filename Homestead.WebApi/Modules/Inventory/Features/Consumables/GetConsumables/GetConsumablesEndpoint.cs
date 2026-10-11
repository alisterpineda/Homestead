using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumables;

public static class GetConsumablesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetConsumablesQuery request,
            GetConsumablesHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("GetConsumables")
        .Produces<PaginatedResponse<ConsumableResponse>>();
    }
}
