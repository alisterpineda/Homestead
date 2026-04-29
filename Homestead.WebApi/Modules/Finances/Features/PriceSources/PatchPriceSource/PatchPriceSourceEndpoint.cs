using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;

public static class PatchPriceSourceEndpoint
{
    public record PatchPriceSourceBody
    {
        public bool? IsEnabled { get; init; }
        public string? Config { get; init; }
        public IReadOnlyList<string>? PairAllowList { get; init; }
    }

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{source}", async (CommodityPriceSource source, PatchPriceSourceBody body,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new PatchPriceSourceCommand
            {
                Source = source,
                IsEnabled = body.IsEnabled,
                Config = body.Config,
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
