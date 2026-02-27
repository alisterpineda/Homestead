using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Homestead.WebApi.Features.Documents.v1.GetDocuments;

public static class GetDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetDocumentsRequest request,
            GetDocumentsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetDocuments")
        .Produces<PaginatedResponse<GetDocumentsResponse>>();
    }
}
