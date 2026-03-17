using Asp.Versioning.Builder;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Admin.v1.AddGroupMember;
using Homestead.WebApi.Features.Admin.v1.BulkDeleteUsers;
using Homestead.WebApi.Features.Admin.v1.ChangeUserPassword;
using Homestead.WebApi.Features.Admin.v1.CreateGroup;
using Homestead.WebApi.Features.Admin.v1.CreateUser;
using Homestead.WebApi.Features.Admin.v1.DeleteGroup;
using Homestead.WebApi.Features.Admin.v1.DeleteUser;
using Homestead.WebApi.Features.Admin.v1.GetGroup;
using Homestead.WebApi.Features.Admin.v1.GetGroups;
using Homestead.WebApi.Features.Admin.v1.GetUsers;
using Homestead.WebApi.Features.Admin.v1.RemoveGroupMember;
using Homestead.WebApi.Features.Admin.v1.UpdateGroup;
using Homestead.WebApi.Features.Admin.v1.UpdateGroupMember;
using Homestead.WebApi.Features.Admin.v1.OcrEngines.BulkDeleteOcrEngines;
using Homestead.WebApi.Features.Admin.v1.OcrEngines.CreateOcrEngine;
using Homestead.WebApi.Features.Admin.v1.OcrEngines.GetOcrEngines;
using Homestead.WebApi.Features.Admin.v1.OcrEngines.UpdateOcrEngine;
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
        ChangeUserPasswordEndpoint.Map(v1);

        var groups = app.MapGroup("/api/v{version:apiVersion}/admin/groups")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        CreateGroupEndpoint.Map(groups);
        GetGroupsEndpoint.Map(groups);
        GetGroupEndpoint.Map(groups);
        UpdateGroupEndpoint.Map(groups);
        DeleteGroupEndpoint.Map(groups);
        AddGroupMemberEndpoint.Map(groups);
        UpdateGroupMemberEndpoint.Map(groups);
        RemoveGroupMemberEndpoint.Map(groups);

        var ocrEngines = app.MapGroup("/api/v{version:apiVersion}/admin/ocr-engines")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetOcrEnginesEndpoint.Map(ocrEngines);
        CreateOcrEngineEndpoint.Map(ocrEngines);
        UpdateOcrEngineEndpoint.Map(ocrEngines);
        BulkDeleteOcrEnginesEndpoint.Map(ocrEngines);

        return app;
    }
}
