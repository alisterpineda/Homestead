using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.GetTextExtractionEngines;

public static class GetTextExtractionEnginesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetTextExtractionEnginesRequest request,
            GetTextExtractionEnginesHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetTextExtractionEngines")
        .Produces<PaginatedResponse<TextExtractionEngineResponse>>();
    }
}
