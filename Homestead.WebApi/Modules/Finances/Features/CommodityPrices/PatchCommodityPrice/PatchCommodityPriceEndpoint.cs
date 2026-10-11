using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.PatchCommodityPrice;

public static class PatchCommodityPriceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchCommodityPriceModel> patchDoc,
            PatchCommodityPriceHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, patchDoc, ct)))
        .WithName("PatchCommodityPrice")
        .Accepts<JsonPatchDocument<PatchCommodityPriceModel>>("application/json-patch+json")
        .Produces<CommodityPriceResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
