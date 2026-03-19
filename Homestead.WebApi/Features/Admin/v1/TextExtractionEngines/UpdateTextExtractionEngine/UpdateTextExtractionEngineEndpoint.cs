namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.UpdateTextExtractionEngine;

public static class UpdateTextExtractionEngineEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateTextExtractionEngineRequest request,
            UpdateTextExtractionEngineHandler handler, CancellationToken ct) =>
        {
            var command = new UpdateTextExtractionEngineCommand { Id = id, Request = request };
            return Results.Ok(await handler.HandleAsync(command, ct));
        })
        .WithName("UpdateTextExtractionEngine")
        .Produces<TextExtractionEngineResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
