using Asp.Versioning.Builder;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Admin.Groups.AddGroupMember;
using Homestead.WebApi.Features.Admin.Users.BulkDeleteUsers;
using Homestead.WebApi.Features.Admin.Users.ChangeUserPassword;
using Homestead.WebApi.Features.Admin.Groups.CreateGroup;
using Homestead.WebApi.Features.Admin.Users.CreateUser;
using Homestead.WebApi.Features.Admin.Groups.DeleteGroup;
using Homestead.WebApi.Features.Admin.Users.DeleteUser;
using Homestead.WebApi.Features.Admin.Groups.GetGroup;
using Homestead.WebApi.Features.Admin.Groups.GetGroups;
using Homestead.WebApi.Features.Admin.Users.GetUsers;
using Homestead.WebApi.Features.Admin.Groups.RemoveGroupMember;
using Homestead.WebApi.Features.Admin.Groups.UpdateGroup;
using Homestead.WebApi.Features.Admin.Groups.UpdateGroupMember;
using Homestead.WebApi.Features.Admin.TextExtractionEngines.BulkDeleteTextExtractionEngines;
using Homestead.WebApi.Features.Admin.TextExtractionEngines.CreateTextExtractionEngine;
using Homestead.WebApi.Features.Admin.TextExtractionEngines.GetTextExtractionEngines;
using Homestead.WebApi.Features.Admin.TextExtractionEngines.UpdateTextExtractionEngine;
using Homestead.WebApi.Features.Admin.Settings.GetDocumentSettings;
using Homestead.WebApi.Features.Admin.Settings.GetSystemSettings;
using Homestead.WebApi.Features.Admin.Settings.PatchDocumentSettings;
using Homestead.WebApi.Features.Admin.Settings.PatchSystemSettings;
using Homestead.WebApi.Features.Admin.Users.UpdateUser;

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
        PatchSystemSettingsEndpoint.Map(settings);

        return app;
    }
}
