namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteTags;

public static class BulkDeleteTagsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteTagsRequest request, BulkDeleteTagsHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one tag ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteTags")
        .Produces<BulkDeleteTagsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
    }
}
