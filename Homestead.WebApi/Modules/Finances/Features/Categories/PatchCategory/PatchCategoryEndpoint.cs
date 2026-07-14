using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.PatchCategory;

public static class PatchCategoryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}",
            async (Guid id, PatchCategoryCommand body, ISender sender, CancellationToken ct) =>
        {
            var request = body with { Id = id };
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("PatchCategory")
        .Produces<CategoryResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
