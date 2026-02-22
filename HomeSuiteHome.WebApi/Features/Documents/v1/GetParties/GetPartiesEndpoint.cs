using HomeSuiteHome.WebApi.Common;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetParties;

public static class GetPartiesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetPartiesRequest request,
            GetPartiesHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetParties")
        .Produces<PaginatedResponse<GetPartiesResponse>>();
    }
}
