using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.Export;

public static class GetExportsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetExportsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetExports")
        .Produces<PaginatedResponse<GetExportsResponse>>()
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
