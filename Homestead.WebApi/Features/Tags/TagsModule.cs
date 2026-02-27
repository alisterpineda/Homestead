using Asp.Versioning.Builder;
using Homestead.WebApi.Features.Tags.v1.CreateTag;
using Homestead.WebApi.Features.Tags.v1.DeleteTag;
using Homestead.WebApi.Features.Tags.v1.GetTags;

namespace Homestead.WebApi.Features.Tags;

public static class TagsModule
{
    public static IVersionedEndpointRouteBuilder MapTagsEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/tags")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Tags")
            .RequireAuthorization();

        CreateTagEndpoint.Map(v1);
        DeleteTagEndpoint.Map(v1);
        GetTagsEndpoint.Map(v1);

        return app;
    }
}
