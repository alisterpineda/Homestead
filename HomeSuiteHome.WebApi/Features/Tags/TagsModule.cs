using Asp.Versioning.Builder;
using HomeSuiteHome.WebApi.Features.Tags.v1.CreateTag;
using HomeSuiteHome.WebApi.Features.Tags.v1.DeleteTag;
using HomeSuiteHome.WebApi.Features.Tags.v1.GetTags;

namespace HomeSuiteHome.WebApi.Features.Tags;

public static class TagsModule
{
    public static IVersionedEndpointRouteBuilder MapTagsEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/tags")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Tags");

        CreateTagEndpoint.Map(v1);
        DeleteTagEndpoint.Map(v1);
        GetTagsEndpoint.Map(v1);

        return app;
    }
}
