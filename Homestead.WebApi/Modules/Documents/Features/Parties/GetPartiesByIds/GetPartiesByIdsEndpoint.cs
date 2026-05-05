using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetPartiesByIds;

public static class GetPartiesByIdsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/resolve", async (GetPartiesByIdsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetPartiesByIds")
        .Produces<IReadOnlyList<GetPartiesByIdsResponse>>();
    }
}
