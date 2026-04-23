using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.CreateCommodityPrice;

public static class CreateCommodityPriceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateCommodityPriceCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/finances/commodity-prices/{response.Id}", response);
        })
        .WithName("CreateCommodityPrice")
        .Produces<CommodityPriceResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
