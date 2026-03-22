using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentVersions;

public static class GetDocumentVersionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid documentId, [AsParameters] PaginatedRequest pagination,
            ISender sender, CancellationToken ct) =>
        {
            var request = new GetDocumentVersionsRequest
            {
                DocumentId = documentId,
                Page = pagination.Page,
                PageSize = pagination.PageSize
            };
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetDocumentVersions")
        .Produces<PaginatedResponse<GetDocumentVersionsResponse>>();
    }
}
