using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.GetNoteSubjects;

public static class GetItemNoteSubjectsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // No per-endpoint policy: the group's ActiveUser admits viewers, like every Inventory GET.
        group.MapGet("/{id:guid}/activity/subjects", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var subjects = await sender.Send(new GetItemNoteSubjectsQuery(id), ct);
            return subjects is null ? Results.NotFound() : Results.Ok(subjects);
        })
        .WithName("GetItemNoteSubjects")
        .Produces<IReadOnlyList<string>>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
