using Asp.Versioning.Builder;
using Homestead.WebApi.Features.Admin.v1.GetUsers;

namespace Homestead.WebApi.Features.Admin;

public static class AdminModule
{
    public static IVersionedEndpointRouteBuilder MapAdminEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/admin/users")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        GetUsersEndpoint.Map(v1);

        return app;
    }
}
