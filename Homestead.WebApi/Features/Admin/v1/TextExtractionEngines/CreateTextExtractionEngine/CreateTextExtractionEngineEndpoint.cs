namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.CreateTextExtractionEngine;

public static class CreateTextExtractionEngineEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateTextExtractionEngineRequest request,
            CreateTextExtractionEngineHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/admin/text-extraction-engines/{response.Id}", response);
        })
        .WithName("CreateTextExtractionEngine")
        .Produces<TextExtractionEngineResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);
    }
}
