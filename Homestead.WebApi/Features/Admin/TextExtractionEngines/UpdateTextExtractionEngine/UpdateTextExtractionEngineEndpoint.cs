using MediatR;

namespace Homestead.WebApi.Features.Admin.TextExtractionEngines.UpdateTextExtractionEngine;

public static class UpdateTextExtractionEngineEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateTextExtractionEngineRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateTextExtractionEngineCommand { Id = id, Request = request };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("UpdateTextExtractionEngine")
        .Produces<TextExtractionEngineResponse>()
        .Produces(StatusCodes.Status409Conflict);
    }
}
