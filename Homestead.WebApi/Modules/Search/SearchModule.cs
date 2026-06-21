using Asp.Versioning.Builder;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Search.Features.UniversalSearch;

namespace Homestead.WebApi.Modules.Search;

public static class SearchModule
{
    public static IVersionedEndpointRouteBuilder MapSearchEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/search")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Search")
            .RequireAuthorization(AuthPolicies.ActiveUser);

        UniversalSearchEndpoint.Map(v1);

        return app;
    }
}
