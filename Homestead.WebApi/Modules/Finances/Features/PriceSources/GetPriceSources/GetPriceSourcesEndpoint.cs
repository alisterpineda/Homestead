namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSources;

public static class GetPriceSourcesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (GetPriceSourcesHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetPriceSourcesQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetPriceSources")
        .Produces<GetPriceSourcesResponse>();
    }
}
