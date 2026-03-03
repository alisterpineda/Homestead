namespace Homestead.WebApi.Features.Admin.v1.DeleteGroup;

public static class DeleteGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id,
            DeleteGroupHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DeleteGroupRequest(id), ct);
            return Results.NoContent();
        })
        .WithName("DeleteGroup")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
