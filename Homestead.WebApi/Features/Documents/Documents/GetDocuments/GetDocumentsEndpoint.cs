using Homestead.WebApi.Common;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Homestead.WebApi.Features.Documents.Documents.GetDocuments;

public static class GetDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetDocumentsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetDocuments")
        .Produces<PaginatedResponse<GetDocumentsResponse>>();
    }
}
