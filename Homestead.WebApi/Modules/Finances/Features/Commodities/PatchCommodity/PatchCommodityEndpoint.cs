using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.PatchCommodity;

public static class PatchCommodityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchCommodityModel> patchDoc,
            PatchCommodityHandler handler, CancellationToken ct) =>
        {
            var command = new PatchCommodityCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("PatchCommodity")
        .Accepts<JsonPatchDocument<PatchCommodityModel>>("application/json-patch+json")
        .Produces<CommodityResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
