namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetParty;

public static class GetPartyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, GetPartyHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(id, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetParty")
        .Produces<GetPartyResponse>();
    }
}
