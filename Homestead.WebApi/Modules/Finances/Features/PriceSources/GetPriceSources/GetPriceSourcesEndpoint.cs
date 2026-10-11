namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSources;

public static class GetPriceSourcesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (GetPriceSourcesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("GetPriceSources")
        .Produces<GetPriceSourcesResponse>();
    }
}
