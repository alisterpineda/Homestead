namespace Homestead.WebApi.Modules.Admin.Features.Schedules.DeleteProcessingSchedule;

public static class DeleteProcessingScheduleEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id,
            DeleteProcessingScheduleHandler handler, CancellationToken ct) =>
        {
            await handler.Handle(new DeleteProcessingScheduleCommand(id), ct);
            return Results.NoContent();
        })
        .WithName("DeleteProcessingSchedule")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
