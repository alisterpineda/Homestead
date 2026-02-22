namespace HomeSuiteHome.WebApi.Features.Documents.v1.DeleteParty;

public static class DeletePartyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeletePartyHandler handler, CancellationToken ct) =>
        {
            var request = new DeletePartyRequest(id);
            var response = await handler.HandleAsync(request, ct);
            return response.Found ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteParty")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
