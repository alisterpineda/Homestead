using Asp.Versioning.Builder;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Admin.v1.BulkDeleteUsers;
using Homestead.WebApi.Features.Admin.v1.CreateUser;
using Homestead.WebApi.Features.Admin.v1.DeleteUser;
using Homestead.WebApi.Features.Admin.v1.GetUsers;
using Homestead.WebApi.Features.Admin.v1.UpdateUser;

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
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetUsersEndpoint.Map(v1);
        CreateUserEndpoint.Map(v1);
        UpdateUserEndpoint.Map(v1);
        DeleteUserEndpoint.Map(v1);
        BulkDeleteUsersEndpoint.Map(v1);

        return app;
    }
}
