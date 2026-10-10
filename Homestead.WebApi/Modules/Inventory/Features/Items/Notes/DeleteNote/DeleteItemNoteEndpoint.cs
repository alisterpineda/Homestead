using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.DeleteNote;

public static class DeleteItemNoteEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}/activity/notes/{noteId:guid}", async (Guid id, Guid noteId,
            DeleteItemNoteHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.Handle(new DeleteItemNoteCommand(id, noteId), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteItemNote")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
