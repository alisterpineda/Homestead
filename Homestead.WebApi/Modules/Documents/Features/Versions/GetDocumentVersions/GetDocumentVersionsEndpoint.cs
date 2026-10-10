using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersions;

public static class GetDocumentVersionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid documentId, [AsParameters] PaginatedRequest pagination,
            GetDocumentVersionsHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentVersionsQuery
            {
                DocumentId = documentId,
                Page = pagination.Page,
                PageSize = pagination.PageSize
            };
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetDocumentVersions")
        .Produces<PaginatedResponse<GetDocumentVersionsResponse>>()
        .RequireDocumentAuthorization(DocumentOperations.Read);
    }
}
