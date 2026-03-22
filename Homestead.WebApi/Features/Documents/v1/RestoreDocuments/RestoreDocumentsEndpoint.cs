using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.RestoreDocuments;

public static class RestoreDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-restore", async (RestoreDocumentsRequest request, ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one document ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkRestoreDocuments")
        .Produces<RestoreDocumentsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAuthorization(AuthPolicies.RequireContributor);

        group.MapPost("/{id:guid}/restore", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var request = new RestoreDocumentsRequest([id]);
            var response = await sender.Send(request, ct);
            return response.RestoredCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("RestoreDocument")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
