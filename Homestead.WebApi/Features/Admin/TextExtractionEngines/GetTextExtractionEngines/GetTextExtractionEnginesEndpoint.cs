using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Admin.TextExtractionEngines.GetTextExtractionEngines;

public static class GetTextExtractionEnginesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetTextExtractionEnginesQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetTextExtractionEngines")
        .Produces<PaginatedResponse<TextExtractionEngineResponse>>();
    }
}
