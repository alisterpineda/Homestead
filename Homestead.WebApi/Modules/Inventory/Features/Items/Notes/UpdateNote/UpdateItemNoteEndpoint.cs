using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.UpdateNote;

public static class UpdateItemNoteEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}/activity/notes/{noteId:guid}", async (Guid id, Guid noteId,
            UpdateItemNoteCommand request, ISender sender, CancellationToken ct) =>
        {
            var command = request with { ItemId = id, NoteId = noteId };
            var response = await sender.Send(command, ct);
            return Results.Ok(response);
        })
        .WithName("UpdateItemNote")
        .Produces<ItemNoteResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
