using Asp.Versioning.Builder;
using HomeSuiteHome.WebApi.Features.Processing.v1.GetProcessingJobs;
using HomeSuiteHome.WebApi.Features.Processing.v1.RetriggerProcessingJob;

namespace HomeSuiteHome.WebApi.Features.Processing;

public static class ProcessingModule
{
    public static IVersionedEndpointRouteBuilder MapProcessingEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/processing/jobs")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Processing");

        GetProcessingJobsEndpoint.Map(v1);
        RetriggerProcessingJobEndpoint.Map(v1);

        return app;
    }
}
