namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.CreateOcrEngine;

public static class CreateOcrEngineEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateOcrEngineRequest request,
            CreateOcrEngineHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/admin/ocr-engines/{response.Id}", response);
        })
        .WithName("CreateOcrEngine")
        .Produces<OcrEngineResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);
    }
}
