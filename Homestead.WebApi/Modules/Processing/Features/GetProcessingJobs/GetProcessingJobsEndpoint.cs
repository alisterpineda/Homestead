using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Processing.Features.GetProcessingJobs;

public static class GetProcessingJobsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetProcessingJobsQuery request,
            GetProcessingJobsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetProcessingJobs")
        .Produces<PaginatedResponse<GetProcessingJobsResponse>>();
    }
}
