using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.GetProcessingSchedules;

public static class GetProcessingSchedulesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetProcessingSchedulesQuery request,
            GetProcessingSchedulesHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetProcessingSchedules")
        .Produces<PaginatedResponse<GetProcessingSchedulesResponse>>();
    }
}
