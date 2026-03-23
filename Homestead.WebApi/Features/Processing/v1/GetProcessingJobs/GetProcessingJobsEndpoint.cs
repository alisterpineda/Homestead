using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Processing.v1.GetProcessingJobs;

public static class GetProcessingJobsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetProcessingJobsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetProcessingJobs")
        .Produces<PaginatedResponse<GetProcessingJobsResponse>>();
    }
}
