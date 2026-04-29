using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSources;

public static class GetPriceSourcesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetPriceSourcesQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetPriceSources")
        .Produces<GetPriceSourcesResponse>();
    }
}
