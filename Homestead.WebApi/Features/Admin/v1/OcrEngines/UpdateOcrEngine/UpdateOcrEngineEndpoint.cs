namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.UpdateOcrEngine;

public static class UpdateOcrEngineEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateOcrEngineRequest request,
            UpdateOcrEngineHandler handler, CancellationToken ct) =>
        {
            var command = new UpdateOcrEngineCommand { Id = id, Request = request };
            return Results.Ok(await handler.HandleAsync(command, ct));
        })
        .WithName("UpdateOcrEngine")
        .Produces<OcrEngineResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
