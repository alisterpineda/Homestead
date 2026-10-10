namespace Homestead.WebApi.Modules.Admin.Features.Groups.DeleteGroup;

public static class DeleteGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id,
            DeleteGroupHandler handler, CancellationToken ct) =>
        {
            await handler.Handle(new DeleteGroupCommand(id), ct);
            return Results.NoContent();
        })
        .WithName("DeleteGroup")
        .Produces(StatusCodes.Status204NoContent);
    }
}
