namespace Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;

public static class GetInventoryOverviewEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/overview", async (GetInventoryOverviewHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.Handle(new GetInventoryOverviewQuery(), ct));
        })
        .WithName("GetInventoryOverview")
        .Produces<GetInventoryOverviewResponse>();
    }
}
