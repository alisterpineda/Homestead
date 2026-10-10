using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Inventory.Features.Tags.BulkDeleteTags;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.DeleteTag;

public static class DeleteTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, BulkDeleteTagsHandler handler, CancellationToken ct) =>
        {
            var request = new BulkDeleteInventoryTagsCommand([id]);
            var response = await handler.Handle(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteInventoryTag")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
