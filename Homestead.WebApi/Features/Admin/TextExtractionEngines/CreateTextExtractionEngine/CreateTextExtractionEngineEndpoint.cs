using MediatR;

namespace Homestead.WebApi.Features.Admin.TextExtractionEngines.CreateTextExtractionEngine;

public static class CreateTextExtractionEngineEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateTextExtractionEngineCommand request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/admin/text-extraction-engines/{response.Id}", response);
        })
        .WithName("CreateTextExtractionEngine")
        .Produces<TextExtractionEngineResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict);
    }
}
