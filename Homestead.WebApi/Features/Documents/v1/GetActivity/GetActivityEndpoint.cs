using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetActivity;

public static class GetActivityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid documentId, [AsParameters] PaginatedRequest pagination,
            ISender sender, CancellationToken ct) =>
        {
            var request = new GetActivityQuery
            {
                DocumentId = documentId,
                Page = pagination.Page,
                PageSize = pagination.PageSize
            };
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("GetDocumentActivity")
        .Produces<PaginatedResponse<GetActivityResponse>>();
    }
}
