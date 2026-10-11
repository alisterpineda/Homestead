using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;

public static class PatchPriceSourceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{source}", async (CommodityPriceSource source, PatchPriceSourceBody body,
            PatchPriceSourceHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(source, body, ct)))
        .WithName("PatchPriceSource")
        .Produces<PatchPriceSourceResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
