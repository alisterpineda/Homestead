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
using Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.BulkDeleteTextExtractionEngines;
using Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.CreateTextExtractionEngine;
using Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.GetTextExtractionEngines;
using Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.UpdateTextExtractionEngine;
using Homestead.WebApi.Features.Admin.v1.Settings.GetDocumentSettings;
using Homestead.WebApi.Features.Admin.v1.Settings.GetSystemSettings;
using Homestead.WebApi.Features.Admin.v1.Settings.PatchDocumentSettings;
using Homestead.WebApi.Features.Admin.v1.Settings.UpdateSystemSettings;
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

        var textExtractionEngines = app.MapGroup("/api/v{version:apiVersion}/admin/text-extraction-engines")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetTextExtractionEnginesEndpoint.Map(textExtractionEngines);
        CreateTextExtractionEngineEndpoint.Map(textExtractionEngines);
        UpdateTextExtractionEngineEndpoint.Map(textExtractionEngines);
        BulkDeleteTextExtractionEnginesEndpoint.Map(textExtractionEngines);

        var settings = app.MapGroup("/api/v{version:apiVersion}/admin/settings")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        GetDocumentSettingsEndpoint.Map(settings);
        PatchDocumentSettingsEndpoint.Map(settings);
        GetSystemSettingsEndpoint.Map(settings);
        UpdateSystemSettingsEndpoint.Map(settings);

        return app;
    }
}
