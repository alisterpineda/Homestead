using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.CreateNote;

public static class CreateItemNoteEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // "activity/notes" rather than "notes": item responses already expose a `notes` property
        // (the Description field's column name), so the nested route avoids the overloaded word.
        group.MapPost("/{id:guid}/activity/notes", async (Guid id, CreateItemNoteCommand request,
            CreateItemNoteHandler handler, CancellationToken ct) =>
        {
            var command = request with { ItemId = id };
            var response = await handler.Handle(command, ct);
            return Results.Created($"/api/v1/inventory/items/{id}/activity/notes/{response.Id}", response);
        })
        .WithName("CreateItemNote")
        .Produces<ItemNoteResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
