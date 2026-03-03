using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Documents.v1.GetActivity;

public static class GetActivityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid documentId, [AsParameters] PaginatedRequest pagination,
            GetActivityHandler handler, CancellationToken ct) =>
        {
            var request = new GetActivityRequest
            {
                DocumentId = documentId,
                Page = pagination.Page,
                PageSize = pagination.PageSize
            };
            return await handler.HandleAsync(request, ct);
        })
        .WithName("GetDocumentActivity")
        .Produces<PaginatedResponse<GetActivityResponse>>();
    }
}
