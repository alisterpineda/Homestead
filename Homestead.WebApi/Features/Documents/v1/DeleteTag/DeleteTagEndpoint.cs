using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Documents.v1.BulkDeleteTags;

namespace Homestead.WebApi.Features.Documents.v1.DeleteTag;

public static class DeleteTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, BulkDeleteTagsHandler handler, CancellationToken ct) =>
        {
            var request = new BulkDeleteTagsRequest([id]);
            var response = await handler.HandleAsync(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteTag")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
