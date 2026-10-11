using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.DeleteCategory;

public static class DeleteCategoryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeleteCategoryHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.HandleAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteCategory")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
