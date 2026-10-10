using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;

public static class RunPriceSourceImportNowEndpoint
{
    public record RunNowBody
    {
        public DateTimeOffset? FromAt { get; init; }
        public DateTimeOffset? ToAt { get; init; }
        public IReadOnlyList<Guid>? CommodityIds { get; init; }
    }

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{source}/runs",
            async (CommodityPriceSource source, RunNowBody? body,
                RunPriceSourceImportNowHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new RunPriceSourceImportNowCommand
            {
                Source = source,
                FromAt = body?.FromAt,
                ToAt = body?.ToAt,
                CommodityIds = body?.CommodityIds,
            }, ct);
            return Results.Ok(response);
        })
        .WithName("RunPriceSourceImportNow")
        .Produces<RunPriceSourceImportNowResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
