using HomeSuiteHome.WebApi.Common;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentRevisions;

public static class GetDocumentRevisionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid documentId, [AsParameters] PaginatedRequest pagination,
            GetDocumentRevisionsHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentRevisionsRequest
            {
                DocumentId = documentId,
                Page = pagination.Page,
                PageSize = pagination.PageSize
            };
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetDocumentRevisions")
        .Produces<PaginatedResponse<GetDocumentRevisionsResponse>>();
    }
}
