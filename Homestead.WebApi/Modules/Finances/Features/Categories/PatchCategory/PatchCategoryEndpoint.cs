using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.PatchCategory;

public static class PatchCategoryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}",
            async (Guid id, PatchCategoryCommand body, PatchCategoryHandler handler, CancellationToken ct) =>
        {
            var request = body with { Id = id };
            return Results.Ok(await handler.Handle(request, ct));
        })
        .WithName("PatchCategory")
        .Produces<CategoryResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
