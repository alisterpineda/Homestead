using Asp.Versioning.Builder;
using HomeSuiteHome.WebApi.Features.Auth.v1.GetAuthConfig;

namespace HomeSuiteHome.WebApi.Features.Auth;

public static class AuthModule
{
    public static IVersionedEndpointRouteBuilder MapAuthEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/auth")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Auth");

        GetAuthConfigEndpoint.Map(v1);

        return app;
    }
}
