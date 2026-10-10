using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.CreateCommodity;

public static class CreateCommodityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateCommodityCommand request, CreateCommodityHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Created($"/api/v1/finances/commodities/{response.Id}", response);
        })
        .WithName("CreateCommodity")
        .Produces<CommodityResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
