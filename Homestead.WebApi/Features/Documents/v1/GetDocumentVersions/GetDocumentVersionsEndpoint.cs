using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentVersions;

public static class GetDocumentVersionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid documentId, [AsParameters] PaginatedRequest pagination,
            GetDocumentVersionsHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentVersionsRequest
            {
                DocumentId = documentId,
                Page = pagination.Page,
                PageSize = pagination.PageSize
            };
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetDocumentVersions")
        .Produces<PaginatedResponse<GetDocumentVersionsResponse>>();
    }
}
