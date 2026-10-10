using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Activity.GetActivity;

public static class GetActivityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid documentId, [AsParameters] PaginatedRequest pagination,
            GetActivityHandler handler, CancellationToken ct) =>
        {
            var request = new GetActivityQuery
            {
                DocumentId = documentId,
                Page = pagination.Page,
                PageSize = pagination.PageSize
            };
            return Results.Ok(await handler.Handle(request, ct));
        })
        .WithName("GetDocumentActivity")
        .Produces<PaginatedResponse<GetActivityResponse>>()
        .RequireDocumentAuthorization(DocumentOperations.Read);
    }
}
