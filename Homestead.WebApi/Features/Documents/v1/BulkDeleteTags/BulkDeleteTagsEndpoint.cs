using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteTags;

public static class BulkDeleteTagsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteTagsCommand request, ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one tag ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteTags")
        .Produces<BulkDeleteTagsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
