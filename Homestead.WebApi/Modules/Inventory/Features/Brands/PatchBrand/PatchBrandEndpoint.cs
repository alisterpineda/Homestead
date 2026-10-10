using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.PatchBrand;

public static class PatchBrandEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchBrandModel> patchDoc,
            PatchBrandHandler handler, CancellationToken ct) =>
        {
            var command = new PatchBrandCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("PatchBrand")
        .Accepts<JsonPatchDocument<PatchBrandModel>>("application/json-patch+json")
        .Produces<BrandResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
