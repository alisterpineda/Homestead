using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;

public static class GetInventoryOverviewEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/overview", async (ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(new GetInventoryOverviewQuery(), ct));
        })
        .WithName("GetInventoryOverview")
        .Produces<GetInventoryOverviewResponse>();
    }
}
