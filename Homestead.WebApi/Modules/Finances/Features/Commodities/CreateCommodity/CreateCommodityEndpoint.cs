using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.CreateCommodity;

public static class CreateCommodityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateCommodityCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/finances/commodities/{response.Id}", response);
        })
        .WithName("CreateCommodity")
        .Produces<CommodityResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
