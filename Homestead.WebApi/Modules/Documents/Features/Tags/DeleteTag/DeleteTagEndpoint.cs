using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Documents.Features.Tags.BulkDeleteTags;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.DeleteTag;

public static class DeleteTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var request = new BulkDeleteTagsCommand([id]);
            var response = await sender.Send(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteTag")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
