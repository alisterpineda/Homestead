namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateParty;

public static class CreatePartyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreatePartyRequest request, CreatePartyHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/documents/parties/{response.Id}", response);
        })
        .WithName("CreateParty")
        .Produces<CreatePartyResponse>(StatusCodes.Status201Created);
    }
}
