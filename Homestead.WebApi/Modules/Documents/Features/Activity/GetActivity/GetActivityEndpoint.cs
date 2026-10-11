using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Activity.GetActivity;

public static class GetActivityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid documentId, [AsParameters] PaginatedRequest pagination,
            GetActivityHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(documentId, pagination, ct)))
        .WithName("GetDocumentActivity")
        .Produces<PaginatedResponse<GetActivityResponse>>()
        .RequireDocumentAuthorization(DocumentOperations.Read);
    }
}
