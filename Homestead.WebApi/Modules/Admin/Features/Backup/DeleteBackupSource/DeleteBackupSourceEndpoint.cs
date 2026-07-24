using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.DeleteBackupSource;

public static class DeleteBackupSourceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/sources/{id:guid}", async (
            Guid id, bool? cancelRun, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteBackupSourceCommand(id, cancelRun ?? false), ct);
            return Results.NoContent();
        })
        .WithName("DeleteBackupSource")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
