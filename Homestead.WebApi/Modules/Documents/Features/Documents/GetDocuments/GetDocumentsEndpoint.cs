using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

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
        .Produces<GetDocumentsResult>();
    }
}
