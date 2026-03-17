using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.GetOcrEngines;

public static class GetOcrEnginesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetOcrEnginesRequest request,
            GetOcrEnginesHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetOcrEngines")
        .Produces<PaginatedResponse<OcrEngineResponse>>();
    }
}
