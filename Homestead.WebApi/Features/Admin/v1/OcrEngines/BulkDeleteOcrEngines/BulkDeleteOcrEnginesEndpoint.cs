namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.BulkDeleteOcrEngines;

public static class BulkDeleteOcrEnginesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteOcrEnginesRequest request,
            BulkDeleteOcrEnginesHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one engine ID must be provided.");

            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteOcrEngines")
        .Produces<BulkDeleteOcrEnginesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
    }
}
