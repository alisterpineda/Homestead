using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.BulkDeleteTextExtractionEngines;

public static class BulkDeleteTextExtractionEnginesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteTextExtractionEnginesRequest request,
            ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one engine ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteTextExtractionEngines")
        .Produces<BulkDeleteTextExtractionEnginesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
    }
}
