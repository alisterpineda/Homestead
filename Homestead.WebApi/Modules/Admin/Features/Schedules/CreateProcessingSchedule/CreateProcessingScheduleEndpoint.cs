namespace Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;

public static class CreateProcessingScheduleEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateProcessingScheduleCommand request,
            CreateProcessingScheduleHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Created($"/api/v1/admin/schedules/{response.Id}", response);
        })
        .WithName("CreateProcessingSchedule")
        .Produces<CreateProcessingScheduleResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);
    }
}
