using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.DeleteGroup;

public static class DeleteGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id,
            ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteGroupCommand(id), ct);
            return Results.NoContent();
        })
        .WithName("DeleteGroup")
        .Produces(StatusCodes.Status204NoContent);
    }
}
