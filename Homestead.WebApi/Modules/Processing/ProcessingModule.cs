using Asp.Versioning.Builder;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Processing.Features.GetProcessingJobs;

using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Processing;

public static class ProcessingModule
{
    public static IVersionedEndpointRouteBuilder MapProcessingEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/processing/jobs")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Processing")
            .RequireAuthorization(AuthPolicies.RequireContributor)
            .WithCommandTransaction();

        GetProcessingJobsEndpoint.Map(v1);

        return app;
    }
}
