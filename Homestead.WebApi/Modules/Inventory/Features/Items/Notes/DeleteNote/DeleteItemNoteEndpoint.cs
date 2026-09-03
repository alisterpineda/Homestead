using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.DeleteNote;

public static class DeleteItemNoteEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}/activity/notes/{noteId:guid}", async (Guid id, Guid noteId,
            ISender sender, CancellationToken ct) =>
        {
            var deleted = await sender.Send(new DeleteItemNoteCommand(id, noteId), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteItemNote")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
