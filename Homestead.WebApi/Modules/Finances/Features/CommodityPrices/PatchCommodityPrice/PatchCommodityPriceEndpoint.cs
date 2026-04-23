using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.PatchCommodityPrice;

public static class PatchCommodityPriceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchCommodityPriceModel> patchDoc,
            ISender sender, CancellationToken ct) =>
        {
            var command = new PatchCommodityPriceCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("PatchCommodityPrice")
        .Accepts<JsonPatchDocument<PatchCommodityPriceModel>>("application/json-patch+json")
        .Produces<CommodityPriceResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
