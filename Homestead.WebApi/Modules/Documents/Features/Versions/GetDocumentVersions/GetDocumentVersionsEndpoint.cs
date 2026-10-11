using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersions;

public static class GetDocumentVersionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid documentId, [AsParameters] PaginatedRequest pagination,
            GetDocumentVersionsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(documentId, pagination, ct)))
        .WithName("GetDocumentVersions")
        .Produces<PaginatedResponse<GetDocumentVersionsResponse>>()
        .RequireDocumentAuthorization(DocumentOperations.Read);
    }
}
