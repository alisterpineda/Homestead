using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Processing.v1.GetProcessingJobs;

public static class GetProcessingJobsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetProcessingJobsRequest request,
            GetProcessingJobsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetProcessingJobs")
        .Produces<PaginatedResponse<GetProcessingJobsResponse>>();
    }
}
