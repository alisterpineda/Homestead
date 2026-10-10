using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.CreateCommodityPrice;

public static class CreateCommodityPriceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateCommodityPriceCommand request, CreateCommodityPriceHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Created($"/api/v1/finances/commodity-prices/{response.Id}", response);
        })
        .WithName("CreateCommodityPrice")
        .Produces<CommodityPriceResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
