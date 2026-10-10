using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetParties;

public static class GetPartiesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetPartiesQuery request,
            GetPartiesHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetParties")
        .Produces<PaginatedResponse<GetPartiesResponse>>();
    }
}
