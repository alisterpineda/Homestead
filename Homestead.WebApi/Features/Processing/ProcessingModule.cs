using Asp.Versioning.Builder;
using Homestead.WebApi.Features.Processing.v1.GetProcessingJobs;

using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Processing;

public static class ProcessingModule
{
    public static IVersionedEndpointRouteBuilder MapProcessingEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/processing/jobs")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Processing")
            .RequireAuthorization(AuthPolicies.RequireContributor);

        GetProcessingJobsEndpoint.Map(v1);

        return app;
    }
}
