using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;

public static class RunPriceSourceImportNowEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{source}/runs",
            async (CommodityPriceSource source, RunNowBody? body,
                RunPriceSourceImportNowHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(source, body, ct)))
        .WithName("RunPriceSourceImportNow")
        .Produces<RunPriceSourceImportNowResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
