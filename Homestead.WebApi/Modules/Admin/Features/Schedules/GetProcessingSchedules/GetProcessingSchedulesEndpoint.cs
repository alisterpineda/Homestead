using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.GetProcessingSchedules;

public static class GetProcessingSchedulesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetProcessingSchedulesQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetProcessingSchedules")
        .Produces<PaginatedResponse<GetProcessingSchedulesResponse>>();
    }
}
