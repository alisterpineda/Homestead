namespace Homestead.WebApi.Features.Tags.v1.DeleteTag;

public static class DeleteTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeleteTagHandler handler, CancellationToken ct) =>
        {
            var request = new DeleteTagRequest(id);
            var response = await handler.HandleAsync(request, ct);
            return response is not null ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteTag")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
