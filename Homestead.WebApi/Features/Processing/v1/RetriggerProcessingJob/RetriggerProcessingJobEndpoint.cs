namespace Homestead.WebApi.Features.Processing.v1.RetriggerProcessingJob;

public static class RetriggerProcessingJobEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/retrigger", async (Guid id,
            RetriggerProcessingJobHandler handler, CancellationToken ct) =>
        {
            var request = new RetriggerProcessingJobRequest(id);
            var response = await handler.HandleAsync(request, ct);
            return response is not null
                ? Results.Created($"/api/v1/processing/jobs/{response.JobId}", response)
                : Results.NotFound();
        })
        .WithName("RetriggerProcessingJob")
        .Produces<RetriggerProcessingJobResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound);
    }
}
