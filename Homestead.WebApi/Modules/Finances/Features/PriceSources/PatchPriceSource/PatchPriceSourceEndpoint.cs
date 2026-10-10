using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;

public static class PatchPriceSourceEndpoint
{
    public record PatchPriceSourceBody
    {
        public IReadOnlyList<string>? PairAllowList { get; init; }
    }

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{source}", async (CommodityPriceSource source, PatchPriceSourceBody body,
            PatchPriceSourceHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new PatchPriceSourceCommand
            {
                Source = source,
                PairAllowList = body.PairAllowList,
            }, ct);
            return Results.Ok(response);
        })
        .WithName("PatchPriceSource")
        .Produces<PatchPriceSourceResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
