using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.Export;

public static class ExportDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (ExportDocumentsCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("ExportDocuments")
        .Produces<ExportDocumentsResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
