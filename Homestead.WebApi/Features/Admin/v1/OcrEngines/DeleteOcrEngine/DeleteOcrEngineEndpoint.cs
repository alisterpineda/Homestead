namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.DeleteOcrEngine;

public static class DeleteOcrEngineEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id,
            DeleteOcrEngineHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DeleteOcrEngineRequest(id), ct);
            return Results.NoContent();
        })
        .WithName("DeleteOcrEngine")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
