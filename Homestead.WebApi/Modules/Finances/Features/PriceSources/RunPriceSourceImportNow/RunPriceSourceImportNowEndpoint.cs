using Homestead.WebApi.Domain.Finances;
using MediatR;

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
                ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new RunPriceSourceImportNowCommand
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
