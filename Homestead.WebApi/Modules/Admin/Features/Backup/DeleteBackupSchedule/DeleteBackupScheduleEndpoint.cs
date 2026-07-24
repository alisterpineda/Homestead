using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.DeleteBackupSchedule;

public static class DeleteBackupScheduleEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/sources/{id:guid}/schedule", async (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteBackupScheduleCommand(id), ct);
            return Results.NoContent();
        })
        .WithName("DeleteBackupSchedule")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
