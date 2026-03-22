using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.GetTextExtractionEngines;

public static class GetTextExtractionEnginesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetTextExtractionEnginesRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetTextExtractionEngines")
        .Produces<PaginatedResponse<TextExtractionEngineResponse>>();
    }
}
