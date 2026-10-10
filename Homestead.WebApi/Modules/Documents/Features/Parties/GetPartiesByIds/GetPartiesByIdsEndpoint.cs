namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetPartiesByIds;

public static class GetPartiesByIdsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/resolve", async (GetPartiesByIdsQuery request,
            GetPartiesByIdsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetPartiesByIds")
        .Produces<IReadOnlyList<GetPartiesByIdsResponse>>();
    }
}
